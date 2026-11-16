using NModbus;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration;

namespace PressureTestApp.Communication.Plc
{
    /// <summary>
    /// 串口服务器（Modbus TCP）配置。一个服务器可挂多个从站（SlaveMap：CSV 文件 → 从站地址）。
    /// </summary>
    public class XdpServerConfig
    {
        /// <summary>服务器 IP 地址。</summary>
        public string IP { get; set; }

        /// <summary>服务器端口。</summary>
        public int Port { get; set; }

        /// <summary>是否启用（1=启用，0=禁用）。</summary>
        public int Enabled { get; set; }

        /// <summary>CSV 文件 → 485 从站地址 的映射。</summary>
        public Dictionary<string, int> SlaveMap { get; set; }
    }

    /// <summary>
    /// Smart200 PLC 配置（旧版单对象字段，兼容用）。
    /// </summary>
    public class XdpSmart200Config
    {
        /// <summary>IP 地址。</summary>
        public string IP { get; set; }

        /// <summary>是否启用。</summary>
        public int Enabled { get; set; }

        /// <summary>CSV 点表文件名。</summary>
        public string CsvFile { get; set; }
    }

    /// <summary>
    /// 信捷 PLC 配置（旧版单对象字段，兼容用）。
    /// </summary>
    public class XinjeConfig
    {
        /// <summary>IP 地址。</summary>
        public string IP { get; set; }

        /// <summary>是否启用。</summary>
        public int Enabled { get; set; }

        /// <summary>CSV 点表文件名。</summary>
        public string CsvFile { get; set; }
    }

    /// <summary>
    /// PLC 类型枚举。
    /// </summary>
    public enum PlcType
    {
        /// <summary>S7-200 SMART PLC。</summary>
        Smart200,

        /// <summary>信捷 XD/XL 系列 PLC。</summary>
        Xinje,

        /// <summary>西门子 S7-1200 PLC。</summary>
        S71200
    }

    /// <summary>
    /// PLC 统一配置（列表中的每一项对应一个 PLC）。
    /// </summary>
    public class PlcConfig
    {
        /// <summary>PLC 类型。</summary>
        public PlcType Type { get; set; }

        /// <summary>IP 地址。</summary>
        public string IP { get; set; }

        /// <summary>是否启用。</summary>
        public int Enabled { get; set; }

        /// <summary>CSV 点表文件名列表（一个 PLC 可加载多个点表）。</summary>
        public List<string> CsvFiles { get; set; } = new List<string>();

        /// <summary>CSV 点表文件名（单文件，兼容旧配置，保存时不再输出）。</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string CsvFile { get; set; }
    }

    /// <summary>
    /// 串口 Modbus RTU 设备配置（一个串口 + 多个从站）。
    /// </summary>
    public class SerialDeviceConfig
    {
        /// <summary>串口名称（如 COM3）。</summary>
        public string PortName { get; set; }

        /// <summary>波特率。</summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>校验位（None/Odd/Even）。</summary>
        public string Parity { get; set; } = "None";

        /// <summary>数据位。</summary>
        public int DataBits { get; set; } = 8;

        /// <summary>停止位（One/Two）。</summary>
        public string StopBits { get; set; } = "One";

        /// <summary>是否启用。</summary>
        public int Enabled { get; set; }

        /// <summary>CSV 文件 → 485 从站地址 的映射。</summary>
        public Dictionary<string, int> SlaveMap { get; set; }
    }

    /// <summary>
    /// 顶层配置对象，对应 OPC/OPCConfig.json。
    /// </summary>
    public class XdpConfig
    {
        /// <summary>串口服务器列表。</summary>
        public List<XdpServerConfig> Servers { get; set; }

        /// <summary>PLC 列表。</summary>
        public List<PlcConfig> Plcs { get; set; }

        /// <summary>串口设备列表。</summary>
        public List<SerialDeviceConfig> SerialDevices { get; set; }

        /// <summary>旧版 Smart200 单对象字段（序列化时为空则不输出）。</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public XdpSmart200Config Smart200 { get; set; }

        /// <summary>旧版信捷单对象字段（序列化时为空则不输出）。</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public XinjeConfig Xinje { get; set; }
    }

    /// <summary>
    /// 配置加载器：从 OPCConfig.json 反序列化 XdpConfig，并兼容旧版 Smart200/Xinje 字段。
    /// </summary>
    public static class XdpConfigLoader
    {
        /// <summary>
        /// 加载配置文件。
        /// </summary>
        /// <param name="jsonPath">OPCConfig.json 完整路径。</param>
        /// <returns>配置对象；文件不存在或解析失败时返回空配置。</returns>
        public static XdpConfig Load(string jsonPath)
        {
            var config = new XdpConfig { Servers = new List<XdpServerConfig>() };

            if (!File.Exists(jsonPath))
            {
                Console.WriteLine("⚠ 未找到配置文件：" + jsonPath);
                return config;
            }

            try
            {
                string json = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                var options = new JsonSerializerOptions();
                options.Converters.Add(new JsonStringEnumConverter());
                var loaded = JsonSerializer.Deserialize<XdpConfig>(json, options);
                if (loaded != null)
                {
                    config.Servers = loaded.Servers;
                    config.Smart200 = loaded.Smart200;
                    config.Xinje = loaded.Xinje;

                    config.Plcs = loaded.Plcs ?? new List<PlcConfig>();
                    config.SerialDevices = loaded.SerialDevices ?? new List<SerialDeviceConfig>();

                    // 兼容旧版：若没有 Plcs 列表，则从旧的 Smart200/Xinje 字段生成
                    if (config.Plcs.Count == 0)
                    {
                        if (loaded.Smart200 != null)
                            config.Plcs.Add(new PlcConfig { Type = PlcType.Smart200, IP = loaded.Smart200.IP, Enabled = loaded.Smart200.Enabled, CsvFile = loaded.Smart200.CsvFile });
                        if (loaded.Xinje != null)
                            config.Plcs.Add(new PlcConfig { Type = PlcType.Xinje, IP = loaded.Xinje.IP, Enabled = loaded.Xinje.Enabled, CsvFile = loaded.Xinje.CsvFile });
                    }

                    // 归一化：把旧的单文件 CsvFile 迁移到 CsvFiles 列表
                    foreach (var plc in config.Plcs)
                    {
                        if (plc.CsvFiles == null)
                            plc.CsvFiles = new List<string>();
                        if (plc.CsvFiles.Count == 0 && !string.IsNullOrWhiteSpace(plc.CsvFile))
                            plc.CsvFiles.Add(plc.CsvFile.Trim());
                        plc.CsvFile = null; // 迁移完成后清空旧字段
                    }
                }
                Console.WriteLine("✅ 已加载配置：共 " + config.Servers.Count + " 个串口服务器");
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ 加载配置失败：" + ex.Message);
            }

            return config;
        }
    }

    /// <summary>
    /// XDP（Modbus）通信器统一接口。
    /// </summary>
    public interface IXdpPlcCommunicator : IDisposable
    {
        /// <summary>是否已连接。</summary>
        bool IsConnected { get; }

        /// <summary>停止后台轮询线程。</summary>
        void Stop();

        /// <summary>连接结果信息。</summary>
        string ConnectionMessage { get; }

        /// <summary>从单个 CSV 文件加载点表。</summary>
        void MappingPlcFromCSV(string csvPath);

        /// <summary>从多个 CSV 文件加载点表。</summary>
        void MappingPlcFromCSV(string[] csvPaths);

        /// <summary>加载从站地址映射。</summary>
        void LoadSlaveIdMap(string jsonPath);

        /// <summary>按标签名写入值。</summary>
        void WriteTag(string tagName, object value);

        /// <summary>按标签名写入值（接口实现）。</summary>
        void WriteDataToPlc(string tagName, object value);

        /// <summary>获取实时点数据列表。</summary>
        List<XdpPlcDataModel> GetPlcStatusList();

        /// <summary>断开连接。</summary>
        void Disconnect();
    }

    /// <summary>
    /// Modbus TCP 通信器（串口服务器）。一个通信器对应一个 IP:端口。
    /// </summary>
    public class XdpPlcCommunicator : IXdpPlcCommunicator
    {
        /// <summary>是否已连接。</summary>
        public bool IsConnected
        {
            get
            {
                if (tcpClient != null)
                {
                    return tcpClient.Connected;
                }
                return false;
            }
        }

        /// <summary>连接结果信息。</summary>
        public string ConnectionMessage
        {
            get { return conStr; }
        }

        /// <summary>获取实时点数据列表。</summary>
        public List<XdpPlcDataModel> GetPlcStatusList()
        {
            return PlcStatusList;
        }

        /// <summary>断开连接并释放资源。</summary>
        public void Disconnect()
        {
            IsExit = true;
            if (WorkThread != null)
            {
                WorkThread.Join(1000);
            }
            if (modbusMaster != null)
            {
                modbusMaster.Dispose();
            }
            if (tcpClient != null)
            {
                tcpClient.Close();
            }
        }

        /// <summary>TCP 客户端。</summary>
        private TcpClient tcpClient = null;

        /// <summary>Modbus TCP 主站。</summary>
        private IModbusMaster modbusMaster = null;

        /// <summary>文件名 → 从站地址 映射。</summary>
        private Dictionary<string, byte> _slaveIdMap = new Dictionary<string, byte>();

        /// <summary>实时点数据列表。</summary>
        public List<XdpPlcDataModel> PlcStatusList;

        /// <summary>连接结果信息。</summary>
        public string conStr = "";

        /// <summary>构造函数：连接串口服务器（默认端口 10001）。</summary>
        public XdpPlcCommunicator(string ip, int port = 10001)
        {
            try
            {
                tcpClient = new TcpClient();
                tcpClient.Connect(ip, port);
                modbusMaster = new ModbusFactory().CreateMaster(tcpClient);
                modbusMaster.Transport.ReadTimeout = 5000;

                if (tcpClient.Connected)
                {
                    Console.WriteLine("✅ MODBUS设备 连接成功！");
                    conStr = "✅ MODBUS设备 连接成功！";
                }
                else
                {
                    Console.WriteLine("❌ MODBUS设备 连接失败！");
                    conStr = "❌ MODBUS设备 连接失败！";
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine("❌ 网络错误：" + ex.Message);
                conStr = "❌ 网络错误：" + ex.Message;
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ 通信出错：" + ex.Message);
                conStr = "❌ 通信出错：" + ex.Message;
            }
        }

        /// <summary>后台轮询线程。</summary>
        Thread WorkThread = null;

        /// <summary>加载完成的点表。</summary>
        List<XdpPlcDataModel> plcDataList;

        /// <summary>是否停止轮询。</summary>
        public bool IsExit = false;

        /// <summary>从单个 CSV 文件加载点表并启动轮询。</summary>
        public void MappingPlcFromCSV(string path)
        {
            if (!IsConnected)
                throw new InvalidOperationException("MODBUS设备 未连接");

            plcDataList = ParseCsv(path);
            // 初值用点列表快照（非 null），实际值由后台线程异步刷新，避免同步读取阻塞加载
            PlcStatusList = new List<XdpPlcDataModel>(plcDataList);
            IsExit = false;
            WorkThread = new Thread(ReadFromPLC);
            if (WorkThread != null)
                WorkThread.Start();
        }

        /// <summary>从多个 CSV 文件加载点表并启动轮询。</summary>
        public void MappingPlcFromCSV(string[] csvPaths)
        {
            if (!IsConnected)
                throw new InvalidOperationException("MODBUS设备 未连接");

            plcDataList = ParseCsv(csvPaths);
            // 初值用点列表快照（非 null），实际值由后台线程异步刷新，避免同步读取阻塞加载
            PlcStatusList = new List<XdpPlcDataModel>(plcDataList);
            IsExit = false;
            WorkThread = new Thread(ReadFromPLC);
            if (WorkThread != null)
                WorkThread.Start();
        }

        /// <summary>后台轮询线程主体，循环读取点数据。</summary>
        void ReadFromPLC()
        {
            if (plcDataList == null) return;
            while (true)
            {
                PlcStatusList = ReadDataFromPlc(plcDataList);
                if (IsExit) break;
                Thread.Sleep(50);
            }
        }

        /// <summary>解析单个 CSV 点表。</summary>
        public List<XdpPlcDataModel> ParseCsv(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("CSV 文件不存在", filePath);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ",",
                MissingFieldFound = args => { }
            };

            using (var reader = new StreamReader(filePath, System.Text.Encoding.UTF8))
            {
                using (var csv = new CsvReader(reader, config))
                {
                    csv.Context.RegisterClassMap<XdpPlcDataModelMap>();

                    try
                    {
                        var plcDataList = csv.GetRecords<XdpPlcDataModel>().ToList();
                        Console.WriteLine("===  CSV 解析完成 ===");
                        Console.WriteLine("总解析条数：" + plcDataList.Count + " 条");
                        if (plcDataList.Count > 0)
                        {
                            Console.WriteLine("示例数据（第一条）：");
                            Console.WriteLine("  变量名称：" + plcDataList[0].TagName);
                            Console.WriteLine("  设备 地址：" + plcDataList[0].PlcAddress);
                            Console.WriteLine("  数据类型：" + plcDataList[0].ValueType);
                            Console.WriteLine("  解析后类型：" + plcDataList[0].AddressType + "（地址：" + plcDataList[0].StartAddress + "）");
                        }

                        return plcDataList;
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("CSV 解析失败：" + ex.Message, ex);
                    }
                }
            }
        }

        /// <summary>解析多个 CSV 点表并合并，按文件名设置从站地址和 TagName 前缀。</summary>
        public List<XdpPlcDataModel> ParseCsv(string[] filePaths)
        {
            var merged = new List<XdpPlcDataModel>();
            foreach (var filePath in filePaths)
            {
                if (!File.Exists(filePath))
                    continue;

                var list = ParseCsv(filePath);
                string prefix = Path.GetFileNameWithoutExtension(filePath) + ".";
                string fileName = Path.GetFileName(filePath);
                byte slaveId = _slaveIdMap.ContainsKey(fileName) ? _slaveIdMap[fileName] : (byte)1;
                if (!_slaveIdMap.ContainsKey(fileName))
                {
                    Console.WriteLine("⚠ 文件 " + fileName + " 未配置站号，使用默认站号 1");
                }

                foreach (var item in list)
                {
                    item.SlaveId = slaveId;
                    item.TagName = prefix + item.TagName;
                    merged.Add(item);
                }
            }

            Console.WriteLine("===  CSV 合并解析完成 ===");
            Console.WriteLine("总解析条数：" + merged.Count + " 条");
            return merged;
        }

        /// <summary>从 OPCConfig.json 加载 文件名 → 从站地址 映射。</summary>
        public void LoadSlaveIdMap(string jsonPath)
        {
            XdpConfig config = XdpConfigLoader.Load(jsonPath);
            _slaveIdMap.Clear();
            foreach (var server in config.Servers)
            {
                if (server.SlaveMap == null) continue;
                foreach (var kv in server.SlaveMap)
                {
                    _slaveIdMap[kv.Key] = (byte)kv.Value;
                }
            }
        }

        /// <summary>读取所有点数据（按每个点各自的 SlaveId 读取）。</summary>
        public List<XdpPlcDataModel> ReadDataFromPlc(List<XdpPlcDataModel> plcDataList)
        {
            foreach (var data in plcDataList)
            {
                try
                {
                    switch (data.AddressType)
                    {
                        case XdpAddressType.Coil:
                            bool[] coilValue = modbusMaster.ReadCoils(data.SlaveId, data.StartAddress, 1);
                            data.Value = coilValue[0];
                            break;

                        case XdpAddressType.HoldingRegister:
                            if (data.ValueType == XdpDataValueType.Float)
                            {
                                // 32 位浮点：高字在前、低字在后
                                ushort[] regs = modbusMaster.ReadHoldingRegisters(data.SlaveId, data.StartAddress, 2);
                                uint combined = ((uint)regs[0] << 16) | regs[1];
                                data.Value = BitConverter.ToSingle(BitConverter.GetBytes(combined), 0);
                            }
                            else if (data.ValueType == XdpDataValueType.DWord)
                            {
                                // 32 位双字：高字在前、低字在后
                                ushort[] regs = modbusMaster.ReadHoldingRegisters(data.SlaveId, data.StartAddress, 2);
                                data.Value = unchecked((int)(((uint)regs[0] << 16) | regs[1]));
                            }
                            else if (data.ValueType == XdpDataValueType.Word)
                            {
                                ushort[] regs = modbusMaster.ReadHoldingRegisters(data.SlaveId, data.StartAddress, 1);
                                data.Value = regs[0];
                            }
                            else if (data.ValueType == XdpDataValueType.Boolean)
                            {
                                ushort[] reg = modbusMaster.ReadHoldingRegisters(data.SlaveId, data.StartAddress, 1);
                                data.Value = ((reg[0] >> data.BitOffset) & 1) == 1;
                            }
                            break;

                        case XdpAddressType.InputRegister:
                            if (data.ValueType == XdpDataValueType.Float)
                            {
                                ushort[] regs = modbusMaster.ReadInputRegisters(data.SlaveId, data.StartAddress, 2);
                                uint combined = ((uint)regs[0] << 16) | regs[1];
                                data.Value = BitConverter.ToSingle(BitConverter.GetBytes(combined), 0);
                            }
                            else
                            {
                                ushort[] regs = modbusMaster.ReadInputRegisters(data.SlaveId, data.StartAddress, 1);
                                data.Value = regs[0];
                            }
                            break;

                        case XdpAddressType.DiscreteInput:
                            bool[] discreteValue = modbusMaster.ReadCoils(data.SlaveId, data.StartAddress, 1);
                            data.Value = discreteValue[0];
                            break;
                    }

                    data.UpdateTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("读取失败 " + data.Description + "：" + ex.Message);
                }
            }
            return plcDataList;
        }

        /// <summary>向 PLC 写入单个点的值。</summary>
        public void WriteDataToPlc(XdpPlcDataModel data)
        {
            if (!IsConnected)
                throw new InvalidOperationException("MODBUS设备 未连接");

            try
            {
                switch (data.AddressType)
                {
                    case XdpAddressType.Coil:
                        if (data.Value is bool)
                        {
                            bool boolValue = (bool)data.Value;
                            modbusMaster.WriteSingleCoil(data.SlaveId, data.StartAddress, boolValue);
                        }
                        break;

                    case XdpAddressType.HoldingRegister:
                        if (data.ValueType == XdpDataValueType.Float)
                        {
                            // 32 位浮点：高字在前、低字在后
                            float floatValue = Convert.ToSingle(data.Value);
                            byte[] floatBytes = BitConverter.GetBytes(floatValue);
                            ushort high = BitConverter.ToUInt16(floatBytes, 2);
                            ushort low = BitConverter.ToUInt16(floatBytes, 0);
                            modbusMaster.WriteMultipleRegisters(data.SlaveId, data.StartAddress, new ushort[] { high, low });
                        }
                        else if (data.ValueType == XdpDataValueType.DWord)
                        {
                            // 32 位双字：高字在前、低字在后
                            int dwordValue = Convert.ToInt32(data.Value);
                            byte[] dwordBytes = BitConverter.GetBytes(dwordValue);
                            ushort high = BitConverter.ToUInt16(dwordBytes, 2);
                            ushort low = BitConverter.ToUInt16(dwordBytes, 0);
                            modbusMaster.WriteMultipleRegisters(data.SlaveId, data.StartAddress, new ushort[] { high, low });
                        }
                        else if (data.ValueType == XdpDataValueType.Word)
                        {
                            ushort wordValue = Convert.ToUInt16(data.Value);
                            modbusMaster.WriteSingleRegister(data.SlaveId, data.StartAddress, wordValue);
                        }
                        else if (data.ValueType == XdpDataValueType.Boolean)
                        {
                            if (data.Value is bool)
                            {
                                bool boolValue = (bool)data.Value;
                                ushort[] current = modbusMaster.ReadHoldingRegisters(data.SlaveId, data.StartAddress, 1);
                                ushort mask = (ushort)(1 << data.BitOffset);
                                ushort newValue = boolValue ? (ushort)(current[0] | mask) : (ushort)(current[0] & ~mask);
                                modbusMaster.WriteSingleRegister(data.SlaveId, data.StartAddress, newValue);
                            }
                        }
                        break;
                }

                Console.WriteLine("成功写入：" + data.Description + "（" + data.PlcAddress + " = " + data.Value + "）");
            }
            catch (Exception ex)
            {
                Console.WriteLine("写入失败 " + data.Description + "：" + ex.Message);
            }
        }

        /// <summary>按标签名写入值。</summary>
        public void WriteTag(string tag, object value)
        {
            if (PlcStatusList == null) return;
            var data = PlcStatusList.FirstOrDefault(d => d.TagName == tag);
            if (data == null) return;
            data.Value = value;
            WriteDataToPlc(data);
        }

        /// <summary>按标签名写入值（接口实现）。</summary>
        public void WriteDataToPlc(string tagName, object value)
        {
            WriteTag(tagName, value);
        }

        /// <summary>停止后台轮询线程。</summary>
        public void Stop()
        {
            IsExit = true;
        }

        private bool _disposed;

        /// <summary>释放资源。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>释放托管/非托管资源。</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                IsExit = true;
                if (WorkThread != null && WorkThread.IsAlive)
                {
                    WorkThread.Join(2000);
                }
            }

            if (modbusMaster != null)
            {
                modbusMaster.Dispose();
            }
            if (tcpClient != null)
            {
                tcpClient.Close();
            }

            _disposed = true;
        }

        /// <summary>析构函数。</summary>
        ~XdpPlcCommunicator()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// Modbus 地址类型枚举。
    /// </summary>
    public enum XdpAddressType
    {
        [Description("线圈（Coil）")]
        Coil,
        [Description("保持寄存器（Holding Register）")]
        HoldingRegister,
        [Description("输入寄存器（Input Register）")]
        InputRegister,
        [Description("离散输入（Discrete Input）")]
        DiscreteInput
    }

    /// <summary>
    /// Modbus 数据值类型枚举。
    /// </summary>
    public enum XdpDataValueType
    {
        [Description("布尔型（位数据）")]
        Boolean,
        [Description("字（16位）")]
        Word,
        [Description("双字（32位）")]
        DWord,
        [Description("浮点型（32位）")]
        Float
    }

    /// <summary>
    /// Modbus 点数据模型（与 CSV 字段对应，并保存运行时值）。
    /// </summary>
    public class XdpPlcDataModel
    {
        /// <summary>变量名。</summary>
        public string TagName { get; set; }

        /// <summary>原始地址（如 H406401、HD1000）。</summary>
        public string PlcAddress { get; set; }

        /// <summary>地址类型。</summary>
        public XdpAddressType AddressType { get; set; }

        /// <summary>Modbus 0-based 起始地址。</summary>
        public ushort StartAddress { get; set; }

        /// <summary>位偏移（按位读写时使用）。</summary>
        public byte BitOffset { get; set; }

        /// <summary>从站地址。</summary>
        public byte SlaveId { get; set; }

        /// <summary>数据值类型。</summary>
        public XdpDataValueType ValueType { get; set; }

        /// <summary>运行时数据值。</summary>
        public object Value { get; set; }

        /// <summary>描述。</summary>
        public string Description { get; set; }

        /// <summary>最后更新时间。</summary>
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// 串口服务器（OPC 风格 H 前缀地址）CSV 点表映射。
    /// </summary>
    public class XdpPlcDataModelMap : ClassMap<XdpPlcDataModel>
    {
        public XdpPlcDataModelMap()
        {
            // 变量名
            Map(m => m.TagName).Name("Tag Name");
            // 原始地址
            Map(m => m.PlcAddress).Name("Address");
            // 描述
            Map(m => m.Description).Name("Description");

            // 数据类型解析
            Map(m => m.ValueType)
                .Name("Data Type")
                .Convert(args =>
                {
                    string dataType = args.Row.GetField<string>("Data Type").ToLower();
                    switch (dataType)
                    {
                        case "boolean":
                        case "布尔":
                            return XdpDataValueType.Boolean;
                        case "word":
                        case "字":
                            return XdpDataValueType.Word;
                        case "dword":
                        case "双字":
                        case "long":
                        case "长整型":
                            return XdpDataValueType.DWord;
                        case "float":
                        case "浮点":
                            return XdpDataValueType.Float;
                        default:
                            return XdpDataValueType.Float;
                    }
                });

            // 地址类型解析（OPC H 前缀：H+区域码+5位16进制地址）
            Map(m => m.AddressType)
                .Name("Address")
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    if (string.IsNullOrEmpty(address))
                        throw new ArgumentException("地址不能为空");

                    if (address.StartsWith("H", StringComparison.OrdinalIgnoreCase))
                    {
                        // OPC 风格地址：H + 区域码(1位) + 5位16进制地址
                        // 区域码: 0=线圈, 1=离散输入, 3=输入寄存器, 4=保持寄存器
                        string body = address.Substring(1);
                        if (body.Length > 0)
                        {
                            switch (body[0])
                            {
                                case '0': return XdpAddressType.Coil;
                                case '1': return XdpAddressType.DiscreteInput;
                                case '3': return XdpAddressType.InputRegister;
                            }
                        }
                        return XdpAddressType.HoldingRegister;
                    }

                    return XdpAddressType.HoldingRegister;
                });

            // 起始地址解析（OPC 1-based 编号转为 Modbus 0-based 地址）
            Map(m => m.StartAddress)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    if (string.IsNullOrEmpty(address))
                        throw new ArgumentException("地址不能为空");

                    if (address.StartsWith("H", StringComparison.OrdinalIgnoreCase))
                    {
                        // OPC 风格地址：H + 区域码(1位) + 5位16进制地址 [+ .位偏移]
                        // 例: H406401 → 保持寄存器 0x06401; H300001 → 输入寄存器 0x00001
                        // 注意：OPC 地址为 1-based 编号（第 1 个寄存器 = 00001），
                        // 而 Modbus 协议地址为 0-based，读取时需减 1 转换。
                        string body = address.Substring(1);
                        int dotIndex = body.IndexOf('.');
                        if (dotIndex >= 0)
                            body = body.Substring(0, dotIndex);
                        string hexPart = body.Length > 1 ? body.Substring(1) : body;
                        ushort addrNum;
                        if (!ushort.TryParse(hexPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addrNum))
                            throw new ArgumentException("H地址格式错误，无法解析起始地址：" + address + "（行，变量" + args.Row.GetField<string>("Tag Name") + "）");
                        return (ushort)(addrNum - 1);
                    }

                    throw new ArgumentException("仅支持 H 前缀地址：" + address + "（行，变量" + args.Row.GetField<string>("Tag Name") + "）");
                });

            // 位偏移解析
            Map(m => m.BitOffset)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    if (string.IsNullOrEmpty(address))
                        return (byte)0;

                    if (address.StartsWith("H", StringComparison.OrdinalIgnoreCase))
                    {
                        string body = address.Substring(1);
                        int dotIndex = body.IndexOf('.');
                        if (dotIndex >= 0 && dotIndex + 1 < body.Length)
                        {
                            byte bit;
                            if (byte.TryParse(body.Substring(dotIndex + 1), out bit))
                                return bit;
                        }
                    }
                    return (byte)0;
                });

            // 数据值不参与 CSV 映射
            Map(m => m.Value)
                .Ignore();

            // 更新时间自动填充
            Map(m => m.UpdateTime)
                .Convert(args => DateTime.Now);
        }
    }

    /// <summary>
    /// Mock 通信器（用于测试/禁用时的模拟数据）。
    /// </summary>
    public class MockXdpPlcCommunicator : IXdpPlcCommunicator
    {
        private readonly List<XdpPlcDataModel> _mockData = new List<XdpPlcDataModel>();
        private readonly Random _random = new Random();
        private bool _initialized;

        public bool IsConnected
        {
            get { return false; }
        }

        public string ConnectionMessage
        {
            get { return "MODBUS设备 未连接（使用模拟数据）"; }
        }

        public void LoadSlaveIdMap(string jsonPath)
        {
            // 模拟通信不执行真实读取，站号映射仅用于真实 PLC 通信
        }

        public void MappingPlcFromCSV(string csvPath)
        {
            MappingPlcFromCSV(new[] { csvPath });
        }

        public void MappingPlcFromCSV(string[] csvPaths)
        {
            if (_initialized)
                return;

            try
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    Delimiter = ",",
                    MissingFieldFound = args => { }
                };

                foreach (var csvPath in csvPaths)
                {
                    if (!File.Exists(csvPath))
                        continue;

                    string prefix = Path.GetFileNameWithoutExtension(csvPath) + ".";

                    using (var reader = new StreamReader(csvPath, System.Text.Encoding.UTF8))
                    {
                        using (var csv = new CsvReader(reader, config))
                        {
                            csv.Context.RegisterClassMap<XdpPlcDataModelMap>();

                            var plcDataList = csv.GetRecords<XdpPlcDataModel>().ToList();

                            foreach (var data in plcDataList)
                            {
                                var mockItem = new XdpPlcDataModel
                                {
                                    TagName = prefix + data.TagName,
                                    SlaveId = 1,
                                    PlcAddress = data.PlcAddress,
                                    AddressType = data.AddressType,
                                    StartAddress = data.StartAddress,
                                    BitOffset = data.BitOffset,
                                    ValueType = data.ValueType,
                                    Description = data.Description,
                                    Value = GenerateMockValue(data),
                                    UpdateTime = DateTime.Now
                                };
                                _mockData.Add(mockItem);
                            }
                        }
                    }
                }

                _initialized = true;
                Console.WriteLine("✅ Mock  模拟数据已加载：" + _mockData.Count + " 个变量");
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Mock  初始化失败：" + ex.Message);
            }
        }

        /// <summary>根据数据类型生成随机模拟值。</summary>
        private object GenerateMockValue(XdpPlcDataModel data)
        {
            switch (data.ValueType)
            {
                case XdpDataValueType.Boolean:
                    return _random.NextDouble() > 0.3;
                case XdpDataValueType.Word:
                    return (ushort)_random.Next(65536);
                case XdpDataValueType.DWord:
                    return _random.Next(int.MinValue, int.MaxValue);
                case XdpDataValueType.Float:
                    return (float)(_random.NextDouble() * 1000);
                default:
                    return 0;
            }
        }

        public void WriteTag(string tagName, object value)
        {
            var item = _mockData.FirstOrDefault(d => string.Equals(d.TagName, tagName, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.Value = value;
                item.UpdateTime = DateTime.Now;
            }
        }

        public void WriteDataToPlc(string tagName, object value)
        {
            WriteTag(tagName, value);
        }

        public List<XdpPlcDataModel> GetPlcStatusList()
        {
            return new List<XdpPlcDataModel>(_mockData);
        }

        public void Stop()
        {
        }

        public void Disconnect()
        {
        }

        public void Dispose()
        {
        }
    }
}
