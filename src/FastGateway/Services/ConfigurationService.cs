using System.Text.Json;
using Core.Entities;
using FastGateway.Infrastructure;
using MessagePack;

namespace FastGateway.Services;

/// <summary>
///     配置文件管理服务，替代EntityFrameworkCore。
///     所有读写均在锁内完成（读返回副本），避免 API 修改配置时并发读到
///     正在变更的 List 导致枚举异常；配置操作频率极低，锁开销可忽略。
/// </summary>
public class ConfigurationService
{
    private readonly string _configPath;
    private readonly Lock _lockObject = new();
    private GatewayConfig _config;
    private long _version;

    /// <summary>
    ///     配置版本号，任意变更后递增；供缓存派生数据的调用方做失效判断
    /// </summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>
    ///     配置持久化完成后触发（静态：网关子应用各自持有独立实例，集群推送只关心"有变更"这一事实）
    /// </summary>
    public static event Action? ConfigurationChanged;

    public ConfigurationService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "data", "gateway.config");

        // 判断目录是否存在，如果不存在则创建
        var directory = Path.GetDirectoryName(_configPath);
        if (directory != null && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

        LoadConfig();
    }

    private void LoadConfig()
    {
        lock (_lockObject)
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.GatewayConfig)
                              ?? new GatewayConfig();
                }
                catch
                {
                    _config = new GatewayConfig();
                }
            }
            else
            {
                _config = new GatewayConfig();
                SaveConfig();
            }

            Interlocked.Increment(ref _version);
        }
    }

    private void SaveConfig()
    {
        lock (_lockObject)
        {
            Interlocked.Increment(ref _version);

            try
            {
                var json = JsonSerializer.Serialize(_config, ConfigJsonContext.Default.GatewayConfig);

                // 原子落盘：先写临时文件再替换，避免写入途中崩溃/断电损坏配置文件
                var tempPath = _configPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _configPath, overwrite: true);
            }
            catch (Exception ex)
            {
                // 磁盘满/权限/文件占用等异常不得冒泡为接口 500，仅记录；内存中的 _config 仍然生效
                Console.WriteLine($"配置保存失败：{ex}");
            }
        }

        // 锁外触发，避免订阅方回调 ExportSnapshot 等带锁方法时死锁
        ConfigurationChanged?.Invoke();
    }

    /// <summary>
    ///     导出配置深拷贝（序列化往返），供集群推送/差异比对使用，不会泄漏内部可变引用
    /// </summary>
    public GatewayConfig ExportSnapshot()
    {
        lock (_lockObject)
        {
            var json = JsonSerializer.Serialize(_config, ConfigJsonContext.Default.GatewayConfig);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.GatewayConfig) ?? new GatewayConfig();
        }
    }

    /// <summary>
    ///     整体替换配置（集群从节点应用主网关快照时使用）
    /// </summary>
    public void ReplaceConfig(GatewayConfig newConfig)
    {
        lock (_lockObject)
        {
            _config = newConfig;
        }

        SaveConfig();
    }

    // Server operations
    public List<Server> GetServers()
    {
        lock (_lockObject)
        {
            return _config.Servers.ToList();
        }
    }

    public Server? GetServer(string id)
    {
        lock (_lockObject)
        {
            return _config.Servers.FirstOrDefault(s => s.Id == id);
        }
    }

    public void AddServer(Server server)
    {
        lock (_lockObject)
        {
            if (string.IsNullOrEmpty(server.Id))
                server.Id = Guid.NewGuid().ToString();

            _config.Servers.Add(server);
            SaveConfig();
        }
    }

    public void UpdateServer(Server server)
    {
        lock (_lockObject)
        {
            var index = _config.Servers.FindIndex(s => s.Id == server.Id);
            if (index >= 0)
            {
                _config.Servers[index] = server;
                SaveConfig();
            }
        }
    }

    public void DeleteServer(string id)
    {
        lock (_lockObject)
        {
            _config.Servers.RemoveAll(s => s.Id == id);
            SaveConfig();
        }
    }

    // DomainName operations
    public List<DomainName> GetDomainNames()
    {
        lock (_lockObject)
        {
            return _config.DomainNames.ToList();
        }
    }

    public DomainName[] GetDomainNamesByServerId(string serverId)
    {
        lock (_lockObject)
        {
            return _config.DomainNames.Where(d => d.ServerId == serverId).ToArray();
        }
    }

    public void AddDomainName(DomainName domainName)
    {
        lock (_lockObject)
        {
            if (string.IsNullOrEmpty(domainName.Id))
                domainName.Id = Guid.NewGuid().ToString();

            _config.DomainNames.Add(domainName);
            SaveConfig();
        }
    }

    public void UpdateDomainName(DomainName domainName)
    {
        lock (_lockObject)
        {
            var index = _config.DomainNames.FindIndex(d => d.Id == domainName.Id);
            if (index >= 0)
            {
                _config.DomainNames[index] = domainName;
                SaveConfig();
            }
        }
    }

    public void DeleteDomainName(string id)
    {
        lock (_lockObject)
        {
            _config.DomainNames.RemoveAll(d => d.Id == id);
            SaveConfig();
        }
    }

    // Cert operations
    public List<Cert> GetCerts()
    {
        lock (_lockObject)
        {
            return _config.Certs.ToList();
        }
    }

    public Cert[] GetActiveCerts()
    {
        lock (_lockObject)
        {
            return _config.Certs.Where(c => !c.Expired).ToArray();
        }
    }

    public void AddCert(Cert cert)
    {
        lock (_lockObject)
        {
            if (string.IsNullOrEmpty(cert.Id))
                cert.Id = Guid.NewGuid().ToString();

            _config.Certs.Add(cert);
            SaveConfig();
        }
    }

    public void UpdateCert(Cert cert)
    {
        lock (_lockObject)
        {
            var index = _config.Certs.FindIndex(c => c.Id == cert.Id);
            if (index >= 0)
            {
                _config.Certs[index] = cert;
                SaveConfig();
            }
        }
    }

    public void DeleteCert(string id)
    {
        lock (_lockObject)
        {
            _config.Certs.RemoveAll(c => c.Id == id);
            SaveConfig();
        }
    }

    // BlacklistAndWhitelist operations
    public List<BlacklistAndWhitelist> GetBlacklistAndWhitelists()
    {
        lock (_lockObject)
        {
            return _config.BlacklistAndWhitelists.ToList();
        }
    }

    public void AddBlacklistAndWhitelist(BlacklistAndWhitelist item)
    {
        lock (_lockObject)
        {
            if (item.Id == 0)
                item.Id = _config.BlacklistAndWhitelists.Count > 0
                    ? _config.BlacklistAndWhitelists.Max(b => b.Id) + 1
                    : 1;

            _config.BlacklistAndWhitelists.Add(item);
            SaveConfig();
        }
    }

    public void UpdateBlacklistAndWhitelist(BlacklistAndWhitelist item)
    {
        lock (_lockObject)
        {
            var index = _config.BlacklistAndWhitelists.FindIndex(b => b.Id == item.Id);
            if (index >= 0)
            {
                _config.BlacklistAndWhitelists[index] = item;
                SaveConfig();
            }
        }
    }

    public void DeleteBlacklistAndWhitelist(long id)
    {
        lock (_lockObject)
        {
            _config.BlacklistAndWhitelists.RemoveAll(b => b.Id == id);
            SaveConfig();
        }
    }

    // RateLimit operations
    public List<RateLimit> GetRateLimits()
    {
        lock (_lockObject)
        {
            return _config.RateLimits.ToList();
        }
    }

    public void AddRateLimit(RateLimit rateLimit)
    {
        lock (_lockObject)
        {
            if (string.IsNullOrEmpty(rateLimit.Id))
                rateLimit.Id = Guid.NewGuid().ToString();

            _config.RateLimits.Add(rateLimit);
            SaveConfig();
        }
    }

    public void UpdateRateLimit(RateLimit rateLimit)
    {
        lock (_lockObject)
        {
            var index = _config.RateLimits.FindIndex(r => r.Id == rateLimit.Id);
            if (index >= 0)
            {
                _config.RateLimits[index] = rateLimit;
                SaveConfig();
            }
        }
    }

    public void DeleteRateLimit(string id)
    {
        lock (_lockObject)
        {
            _config.RateLimits.RemoveAll(r => r.Id == id);
            SaveConfig();
        }
    }

    // StreamForward operations (L4 端口转发)
    public List<StreamForward> GetStreamForwards()
    {
        lock (_lockObject)
        {
            return _config.StreamForwards.ToList();
        }
    }

    public StreamForward? GetStreamForward(string id)
    {
        lock (_lockObject)
        {
            return _config.StreamForwards.FirstOrDefault(s => s.Id == id);
        }
    }

    public void AddStreamForward(StreamForward streamForward)
    {
        lock (_lockObject)
        {
            if (string.IsNullOrEmpty(streamForward.Id))
                streamForward.Id = Guid.NewGuid().ToString();

            _config.StreamForwards.Add(streamForward);
            SaveConfig();
        }
    }

    public void UpdateStreamForward(StreamForward streamForward)
    {
        lock (_lockObject)
        {
            var index = _config.StreamForwards.FindIndex(s => s.Id == streamForward.Id);
            if (index >= 0)
            {
                _config.StreamForwards[index] = streamForward;
                SaveConfig();
            }
        }
    }

    public void DeleteStreamForward(string id)
    {
        lock (_lockObject)
        {
            _config.StreamForwards.RemoveAll(s => s.Id == id);
            SaveConfig();
        }
    }

    // Setting operations
    public List<Setting> GetSettings()
    {
        lock (_lockObject)
        {
            return _config.Settings.ToList();
        }
    }

    public Setting? GetSetting(string key)
    {
        lock (_lockObject)
        {
            return _config.Settings.FirstOrDefault(s => s.Key == key);
        }
    }

    public void AddSetting(Setting setting)
    {
        lock (_lockObject)
        {
            _config.Settings.Add(setting);
            SaveConfig();
        }
    }

    public void UpdateSetting(Setting setting)
    {
        lock (_lockObject)
        {
            var index = _config.Settings.FindIndex(s => s.Key == setting.Key);
            if (index >= 0)
            {
                _config.Settings[index] = setting;
                SaveConfig();
            }
        }
    }

    public void AddOrUpdateSetting(Setting setting)
    {
        lock (_lockObject)
        {
            var index = _config.Settings.FindIndex(s => s.Key == setting.Key);
            if (index >= 0)
                _config.Settings[index] = setting;
            else
                _config.Settings.Add(setting);

            SaveConfig();
        }
    }

    public void DeleteSetting(string key)
    {
        lock (_lockObject)
        {
            _config.Settings.RemoveAll(s => s.Key == key);
            SaveConfig();
        }
    }
}

/// <summary>
///     网关配置数据结构
/// </summary>
[MessagePackObject(true)]
public class GatewayConfig
{
    public List<Server> Servers { get; set; } = new();
    public List<DomainName> DomainNames { get; set; } = new();
    public List<Cert> Certs { get; set; } = new();
    public List<BlacklistAndWhitelist> BlacklistAndWhitelists { get; set; } = new();
    public List<RateLimit> RateLimits { get; set; } = new();
    public List<Setting> Settings { get; set; } = new();
    public List<StreamForward> StreamForwards { get; set; } = new();
}
