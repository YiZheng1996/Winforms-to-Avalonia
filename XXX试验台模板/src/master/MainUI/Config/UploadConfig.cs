namespace MainUI.Config
{
    /// <summary>
    /// 试验数据上传配置。
    /// </summary>
    internal sealed class UploadConfig : IniConfig
    {
        /// <summary>
        /// INI 文件中的固定节名称。所有试验台共用同一套键名，方便现场维护。
        /// </summary>
        private const string SectionName = "上传配置";

        /// <summary>
        /// 初始化上传配置。
        /// 配置文件位于“程序目录\config\UploadConfig.ini”；首次运行不存在时，
        /// 会先创建 config 目录，再将属性默认值写入配置文件。
        /// </summary>
        public UploadConfig()
            : base(Path.Combine(Application.StartupPath, "config", "UploadConfig.ini"))
        {
            SetSectionName(SectionName);

            // 发布后的程序目录不一定预先包含 config 文件夹，因此先确保目录存在。
            string configDirectory = Path.GetDirectoryName(Filename);
            if (!string.IsNullOrWhiteSpace(configDirectory))
                Directory.CreateDirectory(configDirectory);

            if (File.Exists(Filename))
            {
                // 已有配置时以现场配置为准，读取服务器地址和设备信息。
                Load();
            }
            else
            {
                // 首次运行生成模板，服务器地址保持为空，避免误传到错误环境。
                Save();
            }
        }

        /// <summary>
        /// 接口服务器根地址，例如 http://192.168.1.10:5000。
        /// 这里只配置根地址，两个接口的相对路径由 UploadService 统一维护。
        /// </summary>
        [IniKeyName("服务器地址")]
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// 上传到接口的 deviceName 字段。
        /// </summary>
        [IniKeyName("设备名称")]
        public string DeviceName { get; set; } = "XXX试验台";

        /// <summary>
        /// 上传到接口的 deviceCode 字段，由现场为每台设备配置唯一编码。
        /// </summary>
        [IniKeyName("设备编码")]
        public string DeviceCode { get; set; } = string.Empty;

        /// <summary>
        /// 单次 HTTP 请求超时时间，单位为秒；小于 1 的值会在客户端中按 1 秒处理。
        /// </summary>
        [IniKeyName("请求超时秒数")]
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// 自动试验正常结束后的数据上传方式。
        /// Auto：不询问，直接上传一次；Prompt：先询问操作人员是否上传。
        /// </summary>
        [IniKeyName("试验数据上传方式")]
        public string DataUploadMode { get; set; } = "Prompt";
    }
}
