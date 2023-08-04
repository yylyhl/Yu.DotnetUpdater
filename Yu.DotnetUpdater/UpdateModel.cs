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
        /// 热更新：需支持多实例运行，单目录；
        /// <br/>
        /// <br/>
        /// IIS：重叠回收方式（disallowOverlappingRotation=false），
        /// <br/>原文件重命名-->更新文件-->回收应用程序池-->清理重命名的文件
        /// <br/>
        /// <br/>
        /// Service/Nginx：监听端口放在命令参数里(若有)，
        /// <br/>原文件重命名-->解压更新-->启动新实例-->重载nginx设置（若用）-->停止原服务（视情况）
        /// </summary>
        Hot = 1,

        /// <summary>
        /// 热更新：需支持多实例运行，双目录；
        /// <br/>
        /// <br/>
        /// Service/Nginx：监听端口放在配置文件里(若有)，
        /// <br/>停止实例B(若有)-->解压更新（至待启用实例B目录）-->启动实例B-->重载nginx配置（若用）-->停止实例A（视情况）
        /// </summary>
        Hot2 = 2
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
        /// 监听端口，0则为系统服务，大于0则为web服务（主线程监听端口, 子线程监听端口）；
        /// </summary>
        public int[] Ports { get; set; }
        /// <summary>
        /// 备用实例监听端口，用于热更新，说明参考Ports
        /// </summary>
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
