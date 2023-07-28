namespace Yu.DotnetUpdater
{
    internal enum UpdateMode
    {
        /// <summary>
        /// IIS-冷更新：停止应用程序池-->停止站点-->更新文件-->启动应用程序池-->启动站点<br/>
        /// Service/Nginx-冷更新：原文件重命名-->更新文件-->停止服务-->启动服务<br/>
        /// </summary>
        Cold = 0,

        /// <summary>
        /// 新旧服务有共存时间，需避免业务有冲突/并发问题：<br/>
        /// <br/>
        /// IIS-热更新：原文件重命名-->更新文件-->回收应用程序池-->清理重命名的文件<br/>
        /// <br/>
        /// Service/Nginx-热更新(双实例服务+双目录，有同目录文件干扰，有端口监听的提前改好配置)：
        /// 清理重命名的文件-->解压更新（至备份服务目录）-->启动备份服务-->更新重载nginx设置（若用nginx）-->停止原服务（视情况）<br/>
        /// </summary>
        Hot = 2,
        /// <summary>
        /// 新旧服务有共存时间，需避免业务有冲突/并发问题：<br/>
        /// <br/>
        /// Service/Nginx-热更新(双实例服务+单目录，无同目录文件干扰，不适用有端口监听的)：
        /// 清理重命名的文件-->原文件重命名-->解压更新-->启动备份服务-->更新重载nginx设置（若用nginx）-->停止原服务（视情况）<br/>
        /// <br/>
        /// </summary>
        Hot2 = 3
    }
    public class UpdateServiceConf
    {
        /// <summary>
        /// 自动备份目录名称格式
        /// </summary>
        public static string[] BakDirectoryNameDemo => new[] { "yyyyMMdd_HHmmss", "yyyyMMdd_HHmm", "yyyyMMdd_HH", "yyyyMMdd" };
        /// <summary>
        /// 自动备份目录名称，无或不符合则不备份(格式如：yyyyMMdd/yyyyMMdd_HH/yyyyMMdd_HHmm/yyyyMMdd_HHmmss)；
        /// </summary>
        public string BakDirectoryFormat { get; set; }
        /// <summary>
        /// 端口，0则为系统服务，大于0则为web服务；
        /// </summary>
        public int[] Ports { get; set; }
        public int[] BakPorts { get; set; }
        /// <summary>
        /// 更新目标目录，DeployPath下面；
        /// </summary>
        public string Path { get; set; }
        /// <summary>
        /// 实际执行文件名称（不含后缀）。针对在同一主机部署多套服务，目录不同但执行文件一致
        /// </summary>
        public string ExecuteFileName { get; set; }
        /// <summary>
        /// 待更新压缩包，当前目录下；
        /// </summary>
        public string UpdatePack { get; set; }
        /// <summary>
        /// 更新模式
        /// </summary>
        public int UpdateMode { get; set; }
        /// <summary>
        /// 关掉原服务等待秒数，小于1则不关
        /// </summary>
        public int KillOldWaitSeconds { get; set; }

        /// <summary>
        /// [Port=0]，服务名称；
        /// </summary>
        public string ServiceName { get; set; }
        /// <summary>
        /// [Port=0]，服务描述，无则取ServiceName；
        /// </summary>
        public string ServiceDescription { get; set; }

        /// <summary>
        /// 针对用Nginx代理，服务对应的nginx配置文件名；
        /// </summary>
        public string NginxConf { get; set; }

        /// <summary>
        /// [Port>0]，针对IIS，程序池名称；
        /// </summary>
        public string AppPool { get; set; }
        /// <summary>
        /// [Port>0]，针对IIS，站点名称；
        /// </summary>
        public string SiteName { get; set; }
        /// <summary>
        /// [Port>0]，针对IIS，更新后访问一次OpenUrl让后续响应更快；
        /// </summary>
        public string OpenUrl { get; set; }
        /// <summary>
        /// 针对linux系统，开机启动文件；若无需提前配置好才可开机启动；
        /// </summary>
        public string SystemdService { get; set; }
    }
}
