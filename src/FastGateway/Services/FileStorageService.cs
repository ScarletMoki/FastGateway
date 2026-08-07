using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Runtime.InteropServices;
using FastGateway.Dto;
using FastGateway.Infrastructure;

namespace FastGateway.Services;

public static class FileStorageService
{
    private static string GetDriveLetter(string path)
    {
        // 获取完整路径
        var fullPath = Path.GetFullPath(path);

        // 获取盘符
        var driveLetter = Path.GetPathRoot(fullPath);

        return driveLetter;
    }

    // 递归添加目录到ZIP文件
    private static void AddDirectoryToZip(ZipArchive zip, string directoryPath, string entryName)
    {
        var files = Directory.GetFiles(directoryPath);
        var directories = Directory.GetDirectories(directoryPath);

        // 添加文件
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var entryPath = Path.Combine(entryName, fileName).Replace('\\', '/');
            zip.CreateEntryFromFile(file, entryPath);
        }

        // 递归添加子目录
        foreach (var directory in directories)
        {
            var dirName = Path.GetFileName(directory);
            var subEntryName = Path.Combine(entryName, dirName).Replace('\\', '/');
            AddDirectoryToZip(zip, directory, subEntryName);
        }

        // 如果目录为空，创建一个空的目录条目
        if (files.Length == 0 && directories.Length == 0)
        {
            var emptyDirEntry = entryName.Replace('\\', '/') + "/";
            zip.CreateEntry(emptyDirEntry);
        }
    }

    // 递归复制目录
    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        var source = new DirectoryInfo(sourceDir);
        var target = new DirectoryInfo(targetDir);

        // 创建目标目录
        if (!target.Exists) target.Create();

        // 复制所有文件
        foreach (var file in source.GetFiles())
        {
            var targetFile = Path.Combine(target.FullName, file.Name);
            file.CopyTo(targetFile, true);
        }

        // 递归复制子目录
        foreach (var subDir in source.GetDirectories())
        {
            var targetSubDir = Path.Combine(target.FullName, subDir.Name);
            CopyDirectory(subDir.FullName, targetSubDir);
        }
    }

    /// <summary>
    ///     分片上传的暂存目录名。directory 端点会把它从列表里过滤掉。
    ///     刻意放在目标目录下而不是 Path.GetTempPath()：用户往挂载的数据盘传大包时，
    ///     /tmp 常在很小的系统盘或 tmpfs 上，合并还得跨设备整份复制。
    /// </summary>
    private const string UploadStagingDirName = ".fgupload";

    /// <summary>过期分片的保留时长，覆盖上传中途关页面/断网的残留</summary>
    private static readonly TimeSpan StagingRetention = TimeSpan.FromHours(24);

    private static string ResolveStagingDirectory(string targetDirectory, string uploadId)
    {
        // uploadId 直接参与拼路径，必须白名单校验，否则可以用 ../ 穿越出去
        if (string.IsNullOrWhiteSpace(uploadId) || uploadId.Length is < 8 or > 64 ||
            !uploadId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new ValidationException("上传ID非法");

        return Path.Combine(targetDirectory, UploadStagingDirName, uploadId);
    }

    private static void CleanupStaging(string stagingDirectory)
    {
        try
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);

            // 暂存根目录空了就一并删掉，不给用户留垃圾目录
            var root = Path.GetDirectoryName(stagingDirectory);
            if (root != null && Directory.Exists(root) &&
                Path.GetFileName(root) == UploadStagingDirName &&
                !Directory.EnumerateFileSystemEntries(root).Any())
                Directory.Delete(root);
        }
        catch
        {
            // best effort：清理失败不该影响上传结果
        }
    }

    private static void SweepStaleStaging(string targetDirectory)
    {
        try
        {
            var root = Path.Combine(targetDirectory, UploadStagingDirName);
            if (!Directory.Exists(root)) return;

            var deadline = DateTime.UtcNow - StagingRetention;
            foreach (var dir in Directory.GetDirectories(root))
                if (Directory.GetLastWriteTimeUtc(dir) < deadline)
                    Directory.Delete(dir, true);
        }
        catch
        {
            // best effort
        }
    }

    public static IEndpointRouteBuilder MapFileStorage(this IEndpointRouteBuilder app)
    {
        var fileStorage = app.MapGroup("/api/v1/filestorage")
            .WithTags("文件存储")
            .WithDescription("文件存储管理")
            .RequireAuthorization()
            .AddEndpointFilter<ResultFilter>()
            .WithDisplayName("文件存储");

        // 获取系统盘符
        fileStorage.MapGet("drives", () =>
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return DriveInfo.GetDrives().Select(x => new DriveInfoDto
                    {
                        Name = x.Name,
                        DriveType = x.DriveType.ToString(),
                        AvailableFreeSpace = x.AvailableFreeSpace,
                        TotalSize = x.TotalSize,
                        VolumeLabel = x.VolumeLabel,
                        DriveFormat = x.DriveFormat,
                        IsReady = x.IsReady
                    }).ToArray();

                return DriveInfo.GetDrives().Where(x => x.Name == "/").Select(x => new DriveInfoDto
                {
                    Name = x.Name,
                    DriveType = x.DriveType.ToString(),
                    AvailableFreeSpace = x.AvailableFreeSpace,
                    TotalSize = x.TotalSize,
                    VolumeLabel = x.VolumeLabel,
                    DriveFormat = x.DriveFormat,
                    IsReady = x.IsReady
                }).ToArray();
            })
            .WithDescription("获取系统盘符")
            .WithDisplayName("获取系统盘符").WithTags("文件存储");

        // 获取文件夹
        fileStorage.MapGet("directory", (string path, string drives) =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(drives)) throw new ValidationException("盘符不能为空");

            path = Path.Combine(drives, path.TrimStart('/'));

            var directory = new DirectoryInfo(path);
            // 过滤分片暂存目录，避免上传过程中树里冒出一个 .fgupload
            var directories = directory.GetDirectories()
                .Where(x => x.Name != UploadStagingDirName)
                .ToArray();
            var files = directory.GetFiles();
            return new DirectoryListingDto
            {
                Directories = directories.Select(x => new DirectoryInfoDto
                {
                    Name = x.Name,
                    FullName = x.FullName,
                    Extension = x.Extension,
                    CreationTime = x.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    LastAccessTime = x.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    LastWriteTime = x.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Length = 0,
                    IsHidden = x.Attributes.HasFlag(FileAttributes.Hidden),
                    IsSystem = x.Attributes.HasFlag(FileAttributes.System),
                    IsDirectory = x.Attributes.HasFlag(FileAttributes.Directory),
                    IsFile = x.Attributes.HasFlag(FileAttributes.Archive),
                    Drive = GetDriveLetter(x.FullName)
                }).ToArray(),
                Files = files.Select(x => new FileInfoDto
                {
                    Name = x.Name,
                    FullName = x.FullName,
                    Extension = x.Extension,
                    CreationTime = x.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    LastAccessTime = x.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    LastWriteTime = x.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Length = x.Length,
                    IsReadOnly = x.IsReadOnly,
                    IsHidden = x.Attributes.HasFlag(FileAttributes.Hidden),
                    IsSystem = x.Attributes.HasFlag(FileAttributes.System),
                    IsDirectory = x.Attributes.HasFlag(FileAttributes.Directory),
                    IsFile = x.Attributes.HasFlag(FileAttributes.Archive),
                    Drive = GetDriveLetter(x.FullName)
                }).ToArray()
            };
        }).WithDescription("获取文件夹").WithDisplayName("获取文件夹").WithTags("文件存储");

        // 上传文件
        fileStorage.MapPost("upload", async (string path, string drives, HttpContext context) =>
        {
            var file = context.Request.Form.Files.FirstOrDefault();

            if (file == null) throw new ValidationException("文件不能为空");

            if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(drives)) throw new ValidationException("盘符不能为空");

            path = Path.Combine(drives, path.TrimStart('/'));

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            var filePath = Path.Combine(path, file.FileName);
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
        }).WithDescription("上传文件").WithDisplayName("上传文件").WithTags("文件存储");

        // 下载文件
        fileStorage.MapGet("download", (string path, string drives) =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(drives)) throw new ValidationException("盘符不能为空");

            var filePath = Path.Combine(drives, path.TrimStart('/'));

            if (!File.Exists(filePath)) throw new ValidationException("文件不存在");

            var fileInfo = new FileInfo(filePath);

            // 返回 IResult 交给框架写响应体，不再自己 SendFileAsync —— 后者会先把响应发完，
            // 随后 ResultFilter 再写 ResultDto 就会因 headers 已锁定而抛异常。
            //
            // contentType 固定 application/octet-stream，不按扩展名推 MIME：
            //   1) 前端 web/src/utils/fetch.ts 按 content-type 分派，含 json 的会被 JSON.parse、
            //      text/plain 会被 text() 吃掉，只有其它类型才走 response.blob()；
            //   2) 避免浏览器把 .html/.svg 当页面内联渲染。
            //
            // fileDownloadName 内部走 ContentDispositionHeaderValue.SetHttpFileName，会同时产出
            // filename="..."（ASCII 回退）与 filename*=UTF-8''...（RFC 5987），中文名不乱码。
            return TypedResults.PhysicalFile(
                fileInfo.FullName,
                "application/octet-stream",
                fileInfo.Name,
                fileInfo.LastWriteTimeUtc,
                enableRangeProcessing: true);
        }).WithDescription("下载文件").WithDisplayName("下载文件").WithTags("文件存储");

        // 分片上传。path/drives/uploadId/index/total 走 query —— Minimal API 的简单类型只从
        // route/query 绑定，塞在 multipart form 里是拿不到的（与已有的 upload 端点保持一致）。
        // body 只放分片本体。
        fileStorage.MapPost("upload/chunk",
                async (IFormFile file, string path, string drives, string uploadId, int index, int total) =>
                {
                    if (file == null) throw new ValidationException("文件不能为空");

                    if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("路径不能为空");

                    if (string.IsNullOrWhiteSpace(drives)) throw new ValidationException("盘符不能为空");

                    if (total <= 0) throw new ValidationException("分片总数非法");

                    if (index < 0 || index >= total) throw new ValidationException("分片序号非法");

                    path = Path.Combine(drives, path.TrimStart('/'));

                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                    // 第一片时顺手清掉过期残留（关页面/断网导致的孤儿分片）
                    if (index == 0) SweepStaleStaging(path);

                    var stagingDirectory = ResolveStagingDirectory(path, uploadId);
                    Directory.CreateDirectory(stagingDirectory);

                    // 先写 .tmp 再原子改名：请求中断时不会留下“看起来完整”的半个分片，
                    // 合并阶段的存在性检查才可信。
                    var partPath = Path.Combine(stagingDirectory, $"{index:D6}.part");
                    var tempPath = partPath + ".tmp";

                    await using (var stream =
                                 new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await file.CopyToAsync(stream);
                    }

                    File.Move(tempPath, partPath, true);
                })
            .WithDescription("上传文件分片").WithDisplayName("上传文件分片").WithTags("文件存储")
            .DisableAntiforgery();

        fileStorage.MapPost("upload/merge", async (MergeChunksRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            if (string.IsNullOrWhiteSpace(request.FileName)) throw new ValidationException("文件名不能为空");

            if (request.Total <= 0) throw new ValidationException("分片总数非法");

            // 必须是纯文件名，防止 ../ 穿越
            var fileName = Path.GetFileName(request.FileName);
            if (fileName != request.FileName) throw new ValidationException("文件名非法");

            var directoryPath = Path.Combine(request.Drives, request.Path.TrimStart('/'));
            if (!Directory.Exists(directoryPath)) throw new ValidationException("目标目录不存在");

            var stagingDirectory = ResolveStagingDirectory(directoryPath, request.UploadId);
            if (!Directory.Exists(stagingDirectory)) throw new ValidationException("分片不存在或已过期，请重新上传");

            // 按序号构造路径，不用 Directory.GetFiles + OrderBy —— 那是字符串排序，
            // 分片名未零填充时 .10 会排到 .2 前面。
            var parts = Enumerable.Range(0, request.Total)
                .Select(i => Path.Combine(stagingDirectory, $"{i:D6}.part"))
                .ToArray();

            var missing = parts.Count(p => !File.Exists(p));
            if (missing > 0) throw new ValidationException($"缺少 {missing} 个分片，请重新上传");

            var filePath = Path.Combine(directoryPath, fileName);
            var mergingPath = filePath + ".merging";

            try
            {
                await using (var output = new FileStream(mergingPath, FileMode.Create, FileAccess.Write,
                                 FileShare.None, 81920, true))
                {
                    foreach (var part in parts)
                    {
                        await using var input = new FileStream(part, FileMode.Open, FileAccess.Read,
                            FileShare.Read, 81920, true);
                        await input.CopyToAsync(output);
                    }
                }

                // 合并成功才原子替换目标：中途失败不会破坏同名旧文件。
                // 两个人同时传同名文件时各写各的暂存目录，后完成者胜出，
                // 不会产生内容交错的损坏文件。
                File.Move(mergingPath, filePath, true);
            }
            catch
            {
                if (File.Exists(mergingPath)) File.Delete(mergingPath);
                throw;
            }
            finally
            {
                CleanupStaging(stagingDirectory);
            }
        }).WithDescription("合并分片").WithDisplayName("合并分片").WithTags("文件存储");

        // 放弃上传：前端取消或失败时调用，清理残留分片
        fileStorage.MapPost("upload/abort", (AbortUploadRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            var directoryPath = Path.Combine(request.Drives, request.Path.TrimStart('/'));
            CleanupStaging(ResolveStagingDirectory(directoryPath, request.UploadId));
        }).WithDescription("放弃上传").WithDisplayName("放弃上传").WithTags("文件存储");

        // 解压指定的zip文件
        fileStorage.MapPost("unzip", (UnzipRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ValidationException("ZIP文件路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            var zipPath = Path.Combine(request.Drives, request.Path.TrimStart('/'));

            if (!File.Exists(zipPath)) throw new ValidationException("ZIP文件不存在");

            // 检查文件是否为zip文件
            if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("文件不是ZIP格式");

            // 解压到同目录下，创建以文件名命名的文件夹
            var directory = Path.GetDirectoryName(zipPath);
            var extractDirectory = Path.Combine(directory, Path.GetFileNameWithoutExtension(zipPath));

            // 如果解压目录已存在，先删除
            if (Directory.Exists(extractDirectory)) Directory.Delete(extractDirectory, true);

            Directory.CreateDirectory(extractDirectory);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDirectory);
            }
            catch (Exception ex)
            {
                // 如果解压失败，清理创建的目录
                if (Directory.Exists(extractDirectory)) Directory.Delete(extractDirectory, true);
                throw new ValidationException($"解压失败: {ex.Message}");
            }
        }).WithDescription("解压ZIP文件").WithDisplayName("解压ZIP文件").WithTags("文件存储");

        // 批量打包文件为ZIP
        fileStorage.MapPost("create-zip", (CreateZipRequest request) =>
        {
            if (request.SourcePaths == null || request.SourcePaths.Length == 0)
                throw new ValidationException("源文件路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            if (string.IsNullOrWhiteSpace(request.ZipName)) throw new ValidationException("ZIP文件名不能为空");

            // 确保ZIP文件名以.zip结尾
            if (!request.ZipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) request.ZipName += ".zip";

            // 确定ZIP文件的输出路径
            var firstSourcePath = Path.Combine(request.Drives, request.SourcePaths[0].TrimStart('/'));
            var outputDirectory = Path.GetDirectoryName(firstSourcePath);
            var zipPath = Path.Combine(outputDirectory, request.ZipName);

            // 如果ZIP文件已存在，先删除
            if (File.Exists(zipPath)) File.Delete(zipPath);

            // 创建ZIP文件
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            foreach (var sourcePath in request.SourcePaths)
            {
                var fullPath = Path.Combine(request.Drives, sourcePath.TrimStart('/'));

                if (File.Exists(fullPath))
                {
                    // 添加文件到ZIP
                    var fileName = Path.GetFileName(fullPath);
                    zip.CreateEntryFromFile(fullPath, fileName);
                }
                else if (Directory.Exists(fullPath))
                {
                    // 添加目录到ZIP
                    AddDirectoryToZip(zip, fullPath, Path.GetFileName(fullPath));
                }
            }
        }).WithDescription("批量打包文件为ZIP").WithDisplayName("批量打包文件为ZIP").WithTags("文件存储");

        // 打包单个文件或文件夹为ZIP
        fileStorage.MapPost("create-zip-from-path", (CreateZipFromPathRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ValidationException("源路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            if (string.IsNullOrWhiteSpace(request.ZipName)) throw new ValidationException("ZIP文件名不能为空");

            // 确保ZIP文件名以.zip结尾
            if (!request.ZipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) request.ZipName += ".zip";

            var sourcePath = Path.Combine(request.Drives, request.SourcePath.TrimStart('/'));
            var outputDirectory = Path.GetDirectoryName(sourcePath);
            var zipPath = Path.Combine(outputDirectory, request.ZipName);

            // 如果ZIP文件已存在，先删除
            if (File.Exists(zipPath)) File.Delete(zipPath);

            // 创建ZIP文件
            if (File.Exists(sourcePath))
            {
                // 打包单个文件
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                var fileName = Path.GetFileName(sourcePath);
                zip.CreateEntryFromFile(sourcePath, fileName);
            }
            else if (Directory.Exists(sourcePath))
            {
                // 打包整个目录
                ZipFile.CreateFromDirectory(sourcePath, zipPath);
            }
            else
            {
                throw new ValidationException("源路径不存在");
            }
        }).WithDescription("打包单个文件或文件夹为ZIP").WithDisplayName("打包单个文件或文件夹为ZIP").WithTags("文件存储");

        // 删除文件/文件夹

        fileStorage.MapDelete("delete", (string path, string drives) =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(drives)) throw new ValidationException("盘符不能为空");

            path = Path.Combine(drives, path.TrimStart('/'));

            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
        }).WithDescription("删除文件/文件夹").WithDisplayName("删除文件/文件夹").WithTags("文件存储");


        // 重命名
        fileStorage.MapPut("rename", (string path, string drives, string name) =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(drives)) throw new ValidationException("盘符不能为空");

            if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("名称不能为空");

            path = Path.Combine(drives, path.TrimStart('/'));

            if (File.Exists(path))
            {
                var directory = Path.GetDirectoryName(path);
                var newPath = Path.Combine(directory, name);
                File.Move(path, newPath);
            }
            else if (Directory.Exists(path))
            {
                var directory = Path.GetDirectoryName(path);
                var newPath = Path.Combine(directory, name);
                Directory.Move(path, newPath);
            }
        }).WithDescription("重命名").WithDisplayName("重命名").WithTags("文件存储");

        fileStorage.MapPut("create-directory", (string path, string drives, string name) =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(drives)) throw new ValidationException("盘符不能为空");

            path = Path.Combine(drives, path.TrimStart('/'), name);

            if (File.Exists(path)) throw new ValidationException("文件已存在");

            if (Directory.Exists(path)) throw new ValidationException("文件夹已存在");

            Directory.CreateDirectory(path);
        }).WithDescription("创建文件/文件夹").WithDisplayName("创建文件/文件夹").WithTags("文件存储");

        fileStorage.MapGet("property", (string path) =>
        {
            if (string.IsNullOrWhiteSpace(path)) return ResultDto.CreateFailed("路径不能为空");

            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                var fileProperties = new FilePropertyDto
                {
                    Type = "File",
                    Name = fileInfo.Name,
                    FullName = fileInfo.FullName,
                    Extension = fileInfo.Extension,
                    Length = fileInfo.Length,
                    CreationTime = fileInfo.CreationTime,
                    LastAccessTime = fileInfo.LastAccessTime,
                    LastWriteTime = fileInfo.LastWriteTime
                };
                return ResultDto.CreateSuccess(fileProperties);
            }

            if (Directory.Exists(path))
            {
                var directoryInfo = new DirectoryInfo(path);
                var directoryProperties = new FilePropertyDto
                {
                    Type = "Directory",
                    Name = directoryInfo.Name,
                    FullName = directoryInfo.FullName,
                    CreationTime = directoryInfo.CreationTime,
                    LastAccessTime = directoryInfo.LastAccessTime,
                    LastWriteTime = directoryInfo.LastWriteTime
                };
                return ResultDto.CreateSuccess(directoryProperties);
            }

            return ResultDto.CreateFailed("路径不存在");
        }).WithDescription("获取文件或目录属性").WithDisplayName("获取文件或目录属性").WithTags("文件存储");

        // 批量删除文件
        fileStorage.MapPost("delete-multiple", (DeleteMultipleRequest request) =>
        {
            if (request.Items == null || request.Items.Length == 0) throw new ValidationException("删除项不能为空");

            var errors = new List<string>();
            var successCount = 0;

            foreach (var item in request.Items)
                try
                {
                    var path = Path.Combine(item.Drives, item.Path.TrimStart('/'));

                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        successCount++;
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        successCount++;
                    }
                    else
                    {
                        errors.Add($"路径不存在: {item.Path}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"删除失败 {item.Path}: {ex.Message}");
                }

            if (errors.Any())
                throw new ValidationException(
                    $"批量删除完成，成功: {successCount}，失败: {errors.Count}。错误: {string.Join("; ", errors)}");
        }).WithDescription("批量删除文件").WithDisplayName("批量删除文件").WithTags("文件存储");

        // 移动文件或文件夹
        fileStorage.MapPost("move", (MoveFileRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ValidationException("源路径不能为空");

            if (string.IsNullOrWhiteSpace(request.TargetPath)) throw new ValidationException("目标路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            var sourcePath = Path.Combine(request.Drives, request.SourcePath.TrimStart('/'));
            var targetPath = Path.Combine(request.Drives, request.TargetPath.TrimStart('/'));

            // 确保目标目录存在
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetDirectory)) Directory.CreateDirectory(targetDirectory);

            if (File.Exists(sourcePath))
                File.Move(sourcePath, targetPath);
            else if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, targetPath);
            else
                throw new ValidationException("源路径不存在");
        }).WithDescription("移动文件或文件夹").WithDisplayName("移动文件或文件夹").WithTags("文件存储");

        // 复制文件或文件夹
        fileStorage.MapPost("copy", (CopyFileRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ValidationException("源路径不能为空");

            if (string.IsNullOrWhiteSpace(request.TargetPath)) throw new ValidationException("目标路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            var sourcePath = Path.Combine(request.Drives, request.SourcePath.TrimStart('/'));
            var targetPath = Path.Combine(request.Drives, request.TargetPath.TrimStart('/'));

            // 确保目标目录存在
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetDirectory)) Directory.CreateDirectory(targetDirectory);

            if (File.Exists(sourcePath))
                File.Copy(sourcePath, targetPath, true);
            else if (Directory.Exists(sourcePath))
                CopyDirectory(sourcePath, targetPath);
            else
                throw new ValidationException("源路径不存在");
        }).WithDescription("复制文件或文件夹").WithDisplayName("复制文件或文件夹").WithTags("文件存储");

        // 创建文件
        fileStorage.MapPost("create-file", (CreateFileRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            if (string.IsNullOrWhiteSpace(request.FileName)) throw new ValidationException("文件名不能为空");

            var directoryPath = Path.Combine(request.Drives, request.Path.TrimStart('/'));
            var filePath = Path.Combine(directoryPath, request.FileName);

            // 确保目录存在
            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

            // 创建文件
            File.WriteAllText(filePath, request.Content ?? string.Empty);
        }).WithDescription("创建文件").WithDisplayName("创建文件").WithTags("文件存储");

        // 获取文件内容
        fileStorage.MapGet("content", (string path, string drives) =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(drives)) throw new ValidationException("盘符不能为空");

            var filePath = Path.Combine(drives, path.TrimStart('/'));

            if (!File.Exists(filePath)) throw new ValidationException("文件不存在");

            var content = File.ReadAllText(filePath);
            return new FileContentDto { Content = content };
        }).WithDescription("获取文件内容").WithDisplayName("获取文件内容").WithTags("文件存储");

        // 保存文件内容
        fileStorage.MapPost("save-content", (SaveContentRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path)) throw new ValidationException("路径不能为空");

            if (string.IsNullOrWhiteSpace(request.Drives)) throw new ValidationException("盘符不能为空");

            var filePath = Path.Combine(request.Drives, request.Path.TrimStart('/'));

            if (!File.Exists(filePath)) throw new ValidationException("文件不存在");

            File.WriteAllText(filePath, request.Content ?? string.Empty);
        }).WithDescription("保存文件内容").WithDisplayName("保存文件内容").WithTags("文件存储");

        return app;
    }
}