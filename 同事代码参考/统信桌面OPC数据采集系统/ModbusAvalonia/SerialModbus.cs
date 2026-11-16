using NModbus;
using NModbus.Serial;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace PressureTestApp.Communication.Plc
{
    /// <summary>
    /// 串口 Modbus RTU 设备通信器。
    /// 一个串口只打开一次，同一个串口下可挂多个从站（不同 SlaveId），
    /// 每个从站通过 AddCsv 加载各自的 CSV 点表。
    /// </summary>
    public class SerialModbusCommunicator : IXdpPlcCommunicator
    {
        /// <summary>是否已连接（串口处于打开状态）。</summary>
        public bool IsConnected
        {
            get
            {
                if (serialPort != null)
                    return serialPort.IsOpen;
                return false;
            }
        }

        /// <summary>连接结果信息。</summary>
        public string ConnectionMessage => conStr;

        /// <summary>获取当前串口设备下的所有点数据。</summary>
        public List<XdpPlcDataModel> GetPlcStatusList() => PlcStatusList;

        /// <summary>串口对象。</summary>
        private SerialPort serialPort = null;

        /// <summary>NModbus RTU 主站。</summary>
        private IModbusMaster modbusMaster = null;

        /// <summary>加载完成的点表（多个从站合并后的列表）。</summary>
        private List<XdpPlcDataModel> plcDataList = new List<XdpPlcDataModel>();

        /// <summary>后台轮询线程。</summary>
        private Thread WorkThread = null;

        /// <summary>是否停止轮询。</summary>
        public bool IsExit = false;

        /// <summary>实时点数据列表。</summary>
        public List<XdpPlcDataModel> PlcStatusList;

        /// <summary>连接结果信息。</summary>
        public string conStr = "";

        /// <summary>构造函数：根据配置打开串口并创建 RTU 主站。</summary>
        public SerialModbusCommunicator(SerialDeviceConfig cfg)
        {
            try
            {
                if (cfg == null)
                    throw new ArgumentNullException(nameof(cfg));
                if (string.IsNullOrEmpty(cfg.PortName))
                    throw new ArgumentException("串口名称不能为空");

                Parity parity = (Parity)Enum.Parse(typeof(Parity), string.IsNullOrEmpty(cfg.Parity) ? "None" : cfg.Parity, true);
                StopBits stopBits = (StopBits)Enum.Parse(typeof(StopBits), string.IsNullOrEmpty(cfg.StopBits) ? "One" : cfg.StopBits, true);
                int baudRate = cfg.BaudRate > 0 ? cfg.BaudRate : 9600;
                int dataBits = cfg.DataBits > 0 ? cfg.DataBits : 8;

                serialPort = new SerialPort(cfg.PortName, baudRate, parity, dataBits, stopBits);
                serialPort.ReadTimeout = 5000;
                serialPort.WriteTimeout = 5000;
                serialPort.Open();

                modbusMaster = new ModbusFactory().CreateRtuMaster(serialPort);
                modbusMaster.Transport.ReadTimeout = 5000;

                if (serialPort.IsOpen)
                {
                    Console.WriteLine("✅ 串口设备连接成功！" + cfg.PortName);
                    conStr = "✅ 串口设备连接成功！" + cfg.PortName;
                }
                else
                {
                    Console.WriteLine("❌ 串口设备连接失败！" + cfg.PortName);
                    conStr = "❌ 串口设备连接失败！" + cfg.PortName;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ 串口设备通信出错：" + ex.Message);
                conStr = "❌ 串口设备通信出错：" + ex.Message;
            }
        }

        /// <summary>在同一个串口下增加一个从站的 CSV 点表。</summary>
        public void AddCsv(byte slaveId, string csvPath)
        {
            if (!IsConnected) return;
            if (!File.Exists(csvPath)) return;

            var list = ParseCsv(slaveId, csvPath);
            plcDataList.AddRange(list);
            Console.WriteLine($"串口从站 {slaveId} 加载 CSV：{Path.GetFileName(csvPath)}，共 {list.Count} 点");
        }

        /// <summary>所有从站 CSV 加载完成后调用，开始后台轮询读取。</summary>
        public void Start()
        {
            // 初值用点列表快照（非 null），实际值由后台线程异步刷新，避免同步读取阻塞加载
            PlcStatusList = new List<XdpPlcDataModel>(plcDataList);
            IsExit = false;
            WorkThread = new Thread(ReadFromPLC) { IsBackground = true };
            WorkThread.Start();
        }

        /// <summary>从 CSV 加载点表（默认从站 1）并启动轮询。</summary>
        public void MappingPlcFromCSV(string path)
        {
            AddCsv(1, path);
            Start();
        }

        /// <summary>从多个 CSV 加载点表（默认从站 1）并启动轮询。</summary>
        public void MappingPlcFromCSV(string[] csvPaths)
        {
            foreach (var path in csvPaths)
                AddCsv(1, path);
            Start();
        }

        /// <summary>加载从站映射（串口设备站号由配置 SlaveMap 指定，此处无需处理）。</summary>
        public void LoadSlaveIdMap(string jsonPath)
        {
            // 串口设备站号由配置 SlaveId 指定，无需从 OPCConfig.json 映射
        }

        /// <summary>后台轮询线程主体，循环读取所有点数据。</summary>
        private void ReadFromPLC()
        {
            if (plcDataList == null || plcDataList.Count == 0) return;
            while (!IsExit)
            {
                PlcStatusList = ReadDataFromPlc(plcDataList);
                Thread.Sleep(50);
            }
        }

        /// <summary>解析单个 CSV 点表，并为每个点设置从站地址和 TagName 前缀。</summary>
        private List<XdpPlcDataModel> ParseCsv(byte slaveId, string csvPath)
        {
            var list = new List<XdpPlcDataModel>();
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = ",",
                MissingFieldFound = args => { }
            };

            using (var reader = new StreamReader(csvPath, System.Text.Encoding.UTF8))
            using (var csv = new CsvReader(reader, config))
            {
                csv.Context.RegisterClassMap<XdpPlcDataModelMap>();

                var records = csv.GetRecords<XdpPlcDataModel>().ToList();
                string prefix = Path.GetFileNameWithoutExtension(csvPath) + ".";

                foreach (var item in records)
                {
                    item.SlaveId = slaveId;
                    item.TagName = prefix + item.TagName;
                    list.Add(item);
                }
            }

            return list;
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
                                // 保持寄存器读取 32 位浮点：高字在前、低字在后
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
                                // 按位读取：取寄存器指定位
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
                    Console.WriteLine("串口读取失败 " + data.Description + "：" + ex.Message);
                }
            }
            return plcDataList;
        }

        /// <summary>向 PLC 写入单个点的值。</summary>
        public void WriteDataToPlc(XdpPlcDataModel data)
        {
            if (!IsConnected)
                throw new InvalidOperationException("串口设备未连接");

            try
            {
                switch (data.AddressType)
                {
                    case XdpAddressType.Coil:
                        if (data.Value is bool)
                            modbusMaster.WriteSingleCoil(data.SlaveId, data.StartAddress, (bool)data.Value);
                        break;

                    case XdpAddressType.HoldingRegister:
                        if (data.ValueType == XdpDataValueType.Float)
                        {
                            // 写入 32 位浮点：高字在前、低字在后
                            float floatValue = Convert.ToSingle(data.Value);
                            byte[] floatBytes = BitConverter.GetBytes(floatValue);
                            ushort high = BitConverter.ToUInt16(floatBytes, 2);
                            ushort low = BitConverter.ToUInt16(floatBytes, 0);
                            modbusMaster.WriteMultipleRegisters(data.SlaveId, data.StartAddress, new ushort[] { high, low });
                        }
                        else if (data.ValueType == XdpDataValueType.DWord)
                        {
                            // 写入 32 位双字：高字在前、低字在后
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
                            // 按位写入：先读当前寄存器，再修改指定位后写回
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

                Console.WriteLine("串口写入成功：" + data.Description + "（" + data.PlcAddress + " = " + data.Value + "）");
            }
            catch (Exception ex)
            {
                Console.WriteLine("串口写入失败 " + data.Description + "：" + ex.Message);
            }
        }

        /// <summary>按标签名写入值。</summary>
        public void WriteTag(string tag, object value)
        {
            if (PlcStatusList == null) return;
            var data = PlcStatusList.FirstOrDefault(d => d.TagName == tag);
            if (data == null)
                throw new InvalidOperationException("未找到串口设备标签：" + tag);
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

        /// <summary>断开串口连接并释放资源。</summary>
        public void Disconnect()
        {
            IsExit = true;
            if (WorkThread != null)
                WorkThread.Join(1000);
            if (modbusMaster != null)
                modbusMaster.Dispose();
            if (serialPort != null)
            {
                try { serialPort.Close(); } catch { }
            }
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
            if (serialPort != null)
            {
                try { serialPort.Close(); } catch { }
                try { serialPort.Dispose(); } catch { }
            }

            _disposed = true;
        }

        /// <summary>析构函数。</summary>
        ~SerialModbusCommunicator()
        {
            Dispose(false);
        }
    }
}
