namespace MainUI.Config
{
    internal class TCPConfig : IniConfig
    {
        public TCPConfig()
           : base(Application.StartupPath + "\\config\\TCPConfig.ini")
        {
            Load();
        }

        public TCPConfig(string sectionName)
            : base(Application.StartupPath + "\\config\\TCPConfig.ini")
        {
            SetSectionName(sectionName);
            Load();
        }

        /// <summary>
        /// IP地址
        /// </summary>
        [IniKeyName("IP")]
        public string IP { get; set; }

        /// <summary>
        /// 端口号
        /// </summary>
        [IniKeyName("端口号")]
        public string Port { get; set; }

        /// <summary>
        /// 连接超时时间(毫秒)
        /// </summary>
        [IniKeyName("连接超时时间")]
        public int ConnectionTimeout { get; set; } = 10000;

        /// <summary>
        /// 心跳间隔(毫秒)
        /// </summary>
        [IniKeyName("心跳间隔")]
        public int HeartbeatInterval { get; set; } = 5000;

        /// <summary>
        /// 心跳超时时间(毫秒)
        /// </summary>
        [IniKeyName("心跳超时时间")]
        public int HeartbeatTimeout { get; set; } = 15000;
    }
}
