using NModbus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace PressureTestApp.Communication.Plc
{
    /// <summary>
    /// 信捷 PLC 软元件地址解析器（XD/XL 系列 Modbus 映射）
    /// </summary>
    public static class XinjeAddressParser
    {
        /// <summary>
        /// 解析信捷软元件地址，如 HD1000、D1820、M400、HM40、D10.0
        /// </summary>
        public static void Parse(string address, out string prefix, out int number, out int bit)
        {
            prefix = "";
            number = 0;
            bit = 0;

            if (string.IsNullOrEmpty(address))
                throw new ArgumentException("信捷地址不能为空");

            address = address.Trim();

            int i = 0;
            while (i < address.Length && char.IsLetter(address[i]))
                i++;

            if (i == 0)
                throw new ArgumentException("信捷地址缺少区域前缀：" + address);

            prefix = address.Substring(0, i).ToUpperInvariant();

            string rest = address.Substring(i);
            int dot = rest.IndexOf('.');
            string numStr = dot >= 0 ? rest.Substring(0, dot) : rest;

            if (!int.TryParse(numStr, out number) || number < 0)
                throw new ArgumentException("信捷地址编号格式错误：" + address);

            if (dot >= 0)
            {
                if (!int.TryParse(rest.Substring(dot + 1), out bit) || bit < 0 || bit > 15)
                    throw new ArgumentException("信捷位地址错误（0-15）：" + address);
            }
        }

        /// <summary>
        /// 是否为线圈（位）区域
        /// </summary>
        public static bool IsCoil(string prefix)
        {
            switch (prefix)
            {
                case "M":
                case "HM":
                case "SM":
                case "X":
                case "Y":
                case "S":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 根据前缀和编号计算 Modbus 0-based 地址
        /// </summary>
        public static ushort GetModbusAddress(string prefix, int number)
        {
            switch (prefix)
            {
                // 保持寄存器（Hold Registers）
                case "D": return (ushort)number;                    // D0-D20479  -> 0x0000
                case "HD": return (ushort)(41088 + number);         // HD0-HD6143 -> 0xA080
                case "FD": return (ushort)(50368 + number);         // FD0-FD8199 -> 0xC4C0
                case "SD": return (ushort)(28672 + number);         // SD0-SD4095 -> 0x7000

                // 线圈（Coils）
                case "M": return (ushort)number;                    // M0-M20479   -> 0x0000
                case "HM": return (ushort)(49408 + number);         // HM0-HM6143  -> 0xC100
                case "SM": return (ushort)(36864 + number);         // SM0-SM4095  -> 0x9000

                default:
                    throw new ArgumentException("不支持的信捷地址前缀：" + prefix);
            }
        }
    }

    /// <summary>
    /// 信捷 PLC 点表映射：信捷软元件地址 -> XdpPlcDataModel
    /// </summary>
    public class XinjePlcDataModelMap : ClassMap<XdpPlcDataModel>
    {
        public XinjePlcDataModelMap()
        {
            // 变量名
            Map(m => m.TagName).Name("Tag Name");
            // 软元件地址（如 HD1000、D1820）
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

            // 地址类型：线圈或保持寄存器
            Map(m => m.AddressType)
                .Name("Address")
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    XinjeAddressParser.Parse(address, out string prefix, out int number, out int bit);
                    return XinjeAddressParser.IsCoil(prefix)
                        ? XdpAddressType.Coil
                        : XdpAddressType.HoldingRegister;
                });

            // 起始地址（Modbus 0-based 地址）
            Map(m => m.StartAddress)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    XinjeAddressParser.Parse(address, out string prefix, out int number, out int bit);
                    return XinjeAddressParser.GetModbusAddress(prefix, number);
                });

            // 位偏移（如 D10.3 的 3）
            Map(m => m.BitOffset)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    XinjeAddressParser.Parse(address, out string prefix, out int number, out int bit);
                    return (byte)bit;
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
    /// 信捷 PLC（XINJE XD/XL 系列）Modbus TCP 通信器
    /// </summary>
    public class XinjePlcCommunicator : IDisposable
    {
        /// <summary>是否已连接。</summary>
        public bool IsConnected
        {
            get
            {
                if (tcpClient != null)
                    return tcpClient.Connected;
                return false;
            }
        }

        /// <summary>连接结果信息。</summary>
        public string ConnectionMessage => conStr;

        /// <summary>获取信捷 PLC 点数据列表。</summary>
        public List<XdpPlcDataModel> GetPlcStatusList() => PlcStatusList;

        /// <summary>断开连接并释放资源。</summary>
        public void Disconnect()
        {
            IsExit = true;
            if (WorkThread != null)
                WorkThread.Join(1000);
            if (modbusMaster != null)
                modbusMaster.Dispose();
            if (tcpClient != null)
                tcpClient.Close();
        }

        /// <summary>TCP 客户端。</summary>
        private TcpClient tcpClient = null;

        /// <summary>Modbus TCP 主站。</summary>
        private IModbusMaster modbusMaster = null;

        /// <summary>实时点数据列表。</summary>
        public List<XdpPlcDataModel> PlcStatusList;

        /// <summary>连接结果信息。</summary>
        public string conStr = "";

        /// <summary>构造函数：连接信捷 PLC（默认端口 502）。</summary>
        public XinjePlcCommunicator(string ip, int port = 502)
        {
            try
            {
                tcpClient = new TcpClient();
                tcpClient.Connect(ip, port);
                modbusMaster = new ModbusFactory().CreateMaster(tcpClient);
                modbusMaster.Transport.ReadTimeout = 5000;

                if (tcpClient.Connected)
                {
                    Console.WriteLine("✅ 信捷PLC 连接成功！");
                    conStr = "✅ 信捷PLC 连接成功！";
                }
                else
                {
                    Console.WriteLine("❌ 信捷PLC 连接失败！");
                    conStr = "❌ 信捷PLC 连接失败！";
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

        /// <summary>从 CSV 加载点表并启动后台轮询。</summary>
        public void MappingPlcFromCSV(string path)
        {
            if (!IsConnected)
                throw new InvalidOperationException("信捷PLC 未连接");

            plcDataList = ParseCsv(path);
            // 初值用点列表快照（非 null），实际值由后台线程异步刷新，避免同步读取阻塞加载
            PlcStatusList = new List<XdpPlcDataModel>(plcDataList);
            IsExit = false;
            WorkThread = new Thread(ReadFromPLC) { IsBackground = true };
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

        /// <summary>解析信捷 PLC 的 CSV 点表。</summary>
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
            using (var csv = new CsvReader(reader, config))
            {
                csv.Context.RegisterClassMap<XinjePlcDataModelMap>();

                var list = csv.GetRecords<XdpPlcDataModel>().ToList();
                foreach (var item in list)
                {
                    item.SlaveId = 1; // 信捷 Modbus TCP 站号默认 1
                    item.TagName = "PLC_XDP." + item.TagName;
                }

                Console.WriteLine("=== 信捷 CSV 解析完成 ===");
                Console.WriteLine("总解析条数：" + list.Count + " 条");
                return list;
            }
        }

        /// <summary>读取所有点数据。</summary>
        public List<XdpPlcDataModel> ReadDataFromPlc(List<XdpPlcDataModel> plcDataList)
        {
            foreach (var data in plcDataList)
            {
                try
                {
                    if (data.AddressType == XdpAddressType.Coil)
                    {
                        bool[] coilValue = modbusMaster.ReadCoils(data.SlaveId, data.StartAddress, 1);
                        data.Value = coilValue[0];
                    }
                    else if (data.AddressType == XdpAddressType.HoldingRegister)
                    {
                        if (data.ValueType == XdpDataValueType.Float)
                        {
                            // 信捷 REAL 为低字在前、高字在后
                            ushort[] regs = modbusMaster.ReadHoldingRegisters(data.SlaveId, data.StartAddress, 2);
                            uint combined = ((uint)regs[1] << 16) | regs[0];
                            data.Value = BitConverter.ToSingle(BitConverter.GetBytes(combined), 0);
                        }
                        else if (data.ValueType == XdpDataValueType.DWord)
                        {
                            // 信捷 32 位双字为低字在前、高字在后
                            ushort[] regs = modbusMaster.ReadHoldingRegisters(data.SlaveId, data.StartAddress, 2);
                            data.Value = unchecked((int)(((uint)regs[1] << 16) | regs[0]));
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

        /// <summary>向信捷 PLC 写入单个点的值。</summary>
        public void WriteDataToPlc(XdpPlcDataModel data)
        {
            if (!IsConnected)
                throw new InvalidOperationException("信捷PLC 未连接");

            try
            {
                if (data.AddressType == XdpAddressType.Coil)
                {
                    if (data.Value is bool boolValue)
                        modbusMaster.WriteSingleCoil(data.SlaveId, data.StartAddress, boolValue);
                }
                else if (data.AddressType == XdpAddressType.HoldingRegister)
                {
                    if (data.ValueType == XdpDataValueType.Float)
                    {
                        // 信捷 REAL 为低字在前、高字在后
                        float floatValue = Convert.ToSingle(data.Value);
                        byte[] floatBytes = BitConverter.GetBytes(floatValue);
                        ushort high = BitConverter.ToUInt16(floatBytes, 2);
                        ushort low = BitConverter.ToUInt16(floatBytes, 0);
                        modbusMaster.WriteMultipleRegisters(data.SlaveId, data.StartAddress, new ushort[] { low, high });
                    }
                    else if (data.ValueType == XdpDataValueType.DWord)
                    {
                        // 信捷 32 位双字为低字在前、高字在后
                        int dwordValue = Convert.ToInt32(data.Value);
                        byte[] dwordBytes = BitConverter.GetBytes(dwordValue);
                        ushort high = BitConverter.ToUInt16(dwordBytes, 2);
                        ushort low = BitConverter.ToUInt16(dwordBytes, 0);
                        modbusMaster.WriteMultipleRegisters(data.SlaveId, data.StartAddress, new ushort[] { low, high });
                    }
                    else if (data.ValueType == XdpDataValueType.Word)
                    {
                        ushort wordValue = Convert.ToUInt16(data.Value);
                        modbusMaster.WriteSingleRegister(data.SlaveId, data.StartAddress, wordValue);
                    }
                    else if (data.ValueType == XdpDataValueType.Boolean)
                    {
                        if (data.Value is bool boolValue)
                        {
                            ushort[] current = modbusMaster.ReadHoldingRegisters(data.SlaveId, data.StartAddress, 1);
                            ushort mask = (ushort)(1 << data.BitOffset);
                            ushort newValue = boolValue ? (ushort)(current[0] | mask) : (ushort)(current[0] & ~mask);
                            modbusMaster.WriteSingleRegister(data.SlaveId, data.StartAddress, newValue);
                        }
                    }
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
            if (data == null)
                throw new InvalidOperationException("未找到信捷标签：" + tag);
            data.Value = value;
            WriteDataToPlc(data);
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
                    WorkThread.Join(2000);
            }

            if (modbusMaster != null)
                modbusMaster.Dispose();
            if (tcpClient != null)
                tcpClient.Close();

            _disposed = true;
        }

        /// <summary>析构函数。</summary>
        ~XinjePlcCommunicator()
        {
            Dispose(false);
        }
    }
}
