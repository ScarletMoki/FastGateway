using System.ComponentModel.DataAnnotations;

namespace FastGateway.Dto;

/// <summary>
///     批量打包ZIP请求
/// </summary>
public class CreateZipRequest
{
    /// <summary>
    ///     源文件路径数组
    /// </summary>
    [Required(ErrorMessage = "源文件路径不能为空")]
    public string[] SourcePaths { get; set; } = Array.Empty<string>();

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;

    /// <summary>
    ///     ZIP文件名
    /// </summary>
    [Required(ErrorMessage = "ZIP文件名不能为空")]
    public string ZipName { get; set; } = string.Empty;
}

/// <summary>
///     单文件/文件夹打包ZIP请求
/// </summary>
public class CreateZipFromPathRequest
{
    /// <summary>
    ///     源路径
    /// </summary>
    [Required(ErrorMessage = "源路径不能为空")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;

    /// <summary>
    ///     ZIP文件名
    /// </summary>
    [Required(ErrorMessage = "ZIP文件名不能为空")]
    public string ZipName { get; set; } = string.Empty;
}

/// <summary>
///     解压ZIP请求
/// </summary>
public class UnzipRequest
{
    /// <summary>
    ///     ZIP文件路径
    /// </summary>
    [Required(ErrorMessage = "ZIP文件路径不能为空")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;
}

/// <summary>
///     批量删除请求
/// </summary>
public class DeleteMultipleRequest
{
    /// <summary>
    ///     删除项列表
    /// </summary>
    [Required(ErrorMessage = "删除项不能为空")]
    public DeleteItem[] Items { get; set; } = Array.Empty<DeleteItem>();
}

/// <summary>
///     删除项
/// </summary>
public class DeleteItem
{
    /// <summary>
    ///     文件或文件夹路径
    /// </summary>
    [Required(ErrorMessage = "路径不能为空")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;
}

/// <summary>
///     移动文件请求
/// </summary>
public class MoveFileRequest
{
    /// <summary>
    ///     源路径
    /// </summary>
    [Required(ErrorMessage = "源路径不能为空")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    ///     目标路径
    /// </summary>
    [Required(ErrorMessage = "目标路径不能为空")]
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;
}

/// <summary>
///     复制文件请求
/// </summary>
public class CopyFileRequest
{
    /// <summary>
    ///     源路径
    /// </summary>
    [Required(ErrorMessage = "源路径不能为空")]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    ///     目标路径
    /// </summary>
    [Required(ErrorMessage = "目标路径不能为空")]
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;
}

/// <summary>
///     创建文件请求
/// </summary>
public class CreateFileRequest
{
    /// <summary>
    ///     目录路径
    /// </summary>
    [Required(ErrorMessage = "路径不能为空")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;

    /// <summary>
    ///     文件名
    /// </summary>
    [Required(ErrorMessage = "文件名不能为空")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    ///     文件内容
    /// </summary>
    public string? Content { get; set; }
}

/// <summary>
///     保存文件内容请求
/// </summary>
public class SaveContentRequest
{
    /// <summary>
    ///     文件路径
    /// </summary>
    [Required(ErrorMessage = "路径不能为空")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;

    /// <summary>
    ///     文件内容
    /// </summary>
    public string? Content { get; set; }
}

/// <summary>
///     合并上传分片请求
/// </summary>
public class MergeChunksRequest
{
    /// <summary>
    ///     目标目录
    /// </summary>
    [Required(ErrorMessage = "路径不能为空")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;

    /// <summary>
    ///     最终文件名，必须是纯文件名（不含路径分隔符）
    /// </summary>
    [Required(ErrorMessage = "文件名不能为空")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    ///     上传任务 ID，对应分片暂存目录名
    /// </summary>
    [Required(ErrorMessage = "上传ID不能为空")]
    public string UploadId { get; set; } = string.Empty;

    /// <summary>
    ///     分片总数
    /// </summary>
    public int Total { get; set; }
}

/// <summary>
///     放弃上传请求，用于清理残留分片
/// </summary>
public class AbortUploadRequest
{
    /// <summary>
    ///     目标目录
    /// </summary>
    [Required(ErrorMessage = "路径不能为空")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     盘符
    /// </summary>
    [Required(ErrorMessage = "盘符不能为空")]
    public string Drives { get; set; } = string.Empty;

    /// <summary>
    ///     上传任务 ID
    /// </summary>
    [Required(ErrorMessage = "上传ID不能为空")]
    public string UploadId { get; set; } = string.Empty;
}