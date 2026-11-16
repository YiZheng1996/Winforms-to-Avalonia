using CsvHelper;
using CsvHelper.Configuration;
using S7.Net;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace PressureTestApp.Communication.Plc
{
    /// <summary>
    /// 西门子 S7-1200 PLC 通信通用接口
    /// </summary>
    public interface IS71200Communicator : IDisposable
    {
        /// <summary>PLC 连接状态。</summary>
        bool IsConnected { get; }

        /// <summary>停止后台读取线程但保持连接。</summary>
        void Stop();

        /// <summary>PLC 连接结果信息。</summary>
        string ConnectionMessage { get; }

        /// <summary>从 CSV 映射 PLC 变量并启动实时读取。</summary>
        void MappingPlcFromCSV(string csvPath);

        /// <summary>从多个 CSV 文件映射 PLC 变量并启动实时读取。</summary>
        void MappingPlcFromCSV(string[] csvPaths);

        /// <summary>按标签名写入指定值到 PLC。</summary>
        void WriteTag(string tagName, object value);

        /// <summary>获取 PLC 实时数据列表。</summary>
        List<S71200DataModel> GetPlcStatusList();

        /// <summary>断开 PLC 连接。</summary>
        void Disconnect();
    }

    /// <summary>
    /// 西门子 S7-1200 PLC 通信实现（基于 S7netplus）。
    /// 支持 DB 数据块以及 I / Q / M 区域的读写。
    /// </summary>
    public class S71200 : IS71200Communicator
    {
        /// <summary>PLC 连接状态。</summary>
        public bool IsConnected => plc?.IsConnected ?? false;

        /// <summary>连接结果信息。</summary>
        public string ConnectionMessage => conStr;

        /// <summary>获取 PLC 实时数据列表。</summary>
        public List<S71200DataModel> GetPlcStatusList() => PlcStatusList;

        /// <summary>断开 PLC 连接。</summary>
        public void Disconnect()
        {
            IsExit = true;          // 停止读取线程
            WorkThread?.Join(1000); // 等待线程退出
            plc?.Close();           // 关闭 PLC 连接
        }

        /// <summary>S7netplus PLC 连接对象。</summary>
        public S7.Net.Plc plc;

        /// <summary>PLC 实时数据列表。</summary>
        public List<S71200DataModel> PlcStatusList;

        /// <summary>连接结果信息。</summary>
        public string conStr = "";

        /// <summary>构造函数：创建 PLC 对象并尝试建立连接。</summary>
        /// <param name="ip">PLC 的 IP 地址</param>
        public S71200(string ip)
        {
            plc = new S7.Net.Plc(
                CpuType.S71200,   // S7-1200 系列
                ip,               // PLC 的 IP 地址（和电脑同网段）
                0,                // Rack 号（S7-1200 固定为 0）
                1                 // Slot 号（S7-1200 固定为 1）
            );

            try
            {
                Console.WriteLine("正在连接 S7-1200 PLC...");
                plc.Open();

                if (plc.IsConnected)
                {
                    Console.WriteLine("✅ S7-1200 PLC 连接成功！");
                    conStr = "✅ S7-1200 PLC 连接成功！";
                }
                else
                {
                    Console.WriteLine("❌ S7-1200 PLC 连接失败！");
                    conStr = "❌ S7-1200 PLC 连接失败！";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 通信出错：{ex.Message}");
                conStr = $"❌ 通信出错：{ex.Message}";
            }
        }

        /// <summary>后台读取线程。</summary>
        Thread WorkThread = null;

        /// <summary>从 CSV 解析出的点表。</summary>
        List<S71200DataModel> plcDataList;

        /// <summary>是否停止后台读取线程。</summary>
        public bool IsExit = false;

        /// <summary>从 CSV 映射 PLC 变量并启动后台实时读取。</summary>
        /// <param name="path">CSV 文件路径</param>
        public void MappingPlcFromCSV(string path)
        {
            MappingPlcFromCSV(new[] { path });
        }

        /// <summary>从多个 CSV 文件映射 PLC 变量并启动后台实时读取。</summary>
        /// <param name="csvPaths">CSV 文件路径数组</param>
        public void MappingPlcFromCSV(string[] csvPaths)
        {
            if (!plc.IsConnected)
                throw new InvalidOperationException("S7-1200 PLC 未连接");

            plcDataList = ParseCsv(csvPaths);
            // 初值用点列表快照（非 null），实际值由后台线程异步刷新，避免同步读取阻塞加载
            PlcStatusList = new List<S71200DataModel>(plcDataList);
            IsExit = false;
            WorkThread = new Thread(ReadFromPLC) { IsBackground = true };
            WorkThread.Start();
        }

        /// <summary>后台读取线程主体，循环读取 PLC 数据。</summary>
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

        /// <summary>解析 S7-1200 的 CSV 点表。</summary>
        /// <param name="filePath">CSV 文件路径</param>
        /// <returns>解析后的 PLC 数据列表</returns>
        public List<S71200DataModel> ParseCsv(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("CSV 文件不存在", filePath);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,                    // 首行为表头
                Delimiter = ",",                           // CSV 分隔符
                Encoding = System.Text.Encoding.UTF8,      // 支持中文
                MissingFieldFound = args =>
                {
                    throw new Exception($"缺失必填字段：{args.HeaderNames.First()}（行{args.Context.Parser.Row}）");
                },
                BadDataFound = args =>
                {
                    Console.WriteLine($"警告：行 {args.Context.Parser.Row} 数据格式错误，原始数据：{args.RawRecord}");
                }
            };

            using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);
            using var csv = new CsvReader(reader, config, leaveOpen: false);

            csv.Context.RegisterClassMap<S71200DataModelMap>();

            try
            {
                var list = csv.GetRecords<S71200DataModel>().ToList();

                Console.WriteLine("=== S7-1200 CSV 解析完成 ===");
                Console.WriteLine($"总解析条数：{list.Count} 条");
                if (list.Count > 0)
                {
                    Console.WriteLine("示例数据（第一条）：");
                    Console.WriteLine($"  变量名称：{list[0].TagName}");
                    Console.WriteLine($"  PLC 地址：{list[0].PlcAddress}");
                    Console.WriteLine($"  数据类型：{list[0].ValueType}");
                    Console.WriteLine($"  数据区域：{list[0].Area}（DB{list[0].DbNumber}，字节：{list[0].StartByte}，位：{list[0].Bit}）");
                }

                return list;
            }
            catch (Exception ex)
            {
                throw new Exception($"CSV 解析失败：{ex.Message}", ex);
            }
        }

        /// <summary>解析多个 S7-1200 CSV 点表并合并为一个列表。</summary>
        /// <param name="filePaths">CSV 文件路径数组</param>
        /// <returns>合并后的 PLC 数据列表</returns>
        public List<S71200DataModel> ParseCsv(string[] filePaths)
        {
            var merged = new List<S71200DataModel>();
            foreach (var filePath in filePaths)
            {
                if (!File.Exists(filePath))
                    continue;

                merged.AddRange(ParseCsv(filePath));
            }

            Console.WriteLine($"=== S7-1200 CSV 合并解析完成：共 {merged.Count} 条 ===");
            return merged;
        }

        /// <summary>把 S7-1200 数据区域转换为 S7.Net 的 DataType。</summary>
        private S7.Net.DataType ToS7DataType(S71200Area area)
        {
            return area switch
            {
                S71200Area.Input => S7.Net.DataType.Input,
                S71200Area.Output => S7.Net.DataType.Output,
                S71200Area.Memory => S7.Net.DataType.Memory,
                _ => S7.Net.DataType.DataBlock
            };
        }

        /// <summary>取数据块编号：非 DB 区域（I/Q/M）固定为 0。</summary>
        private int GetDbNumber(S71200DataModel data)
        {
            return data.Area == S71200Area.DataBlock ? data.DbNumber : 0;
        }

        /// <summary>从 PLC 读取数据。</summary>
        /// <param name="list">解析后的 PLC 数据列表</param>
        public List<S71200DataModel> ReadDataFromPlc(List<S71200DataModel> list)
        {
            // 按（区域，数据块号）分组后整块读取，避免逐点请求导致刷新过慢
            foreach (var group in list.GroupBy(d => new { d.Area, Db = GetDbNumber(d) }))
            {
                S7.Net.DataType s7DataType = ToS7DataType(group.Key.Area);
                int db = group.Key.Db;

                int minByte = group.Min(d => d.StartByte);
                int maxByte = group.Max(d => d.StartByte + GetByteLength(d) - 1);

                byte[] buffer;
                try
                {
                    buffer = plc.ReadBytes(s7DataType, db, minByte, maxByte - minByte + 1);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"批量读取失败 Area={group.Key.Area} DB={db}：{ex.Message}");
                    continue;
                }

                foreach (var data in group)
                {
                    try
                    {
                        int offset = data.StartByte - minByte;
                        data.Value = ParseValue(buffer, offset, data.ValueType, data.Bit);
                        data.UpdateTime = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"解析失败 {data.Description}：{ex.Message}");
                    }
                }
            }
            return list;
        }

        /// <summary>根据数据类型返回占用的字节数。</summary>
        private static int GetByteLength(S71200DataModel data)
        {
            return data.ValueType switch
            {
                PlcDataValueType.Boolean => 1,
                PlcDataValueType.Byte => 1,
                PlcDataValueType.Int => 2,
                PlcDataValueType.Word => 2,
                PlcDataValueType.DWord => 4,
                PlcDataValueType.Long => 4,
                PlcDataValueType.Float => 4,
                _ => 4
            };
        }

        /// <summary>从大端字节缓冲区解析指定类型的值（S7 协议为大端序，位序为 LSB）。</summary>
        private static object ParseValue(byte[] buffer, int offset, PlcDataValueType valueType, int bit)
        {
            switch (valueType)
            {
                case PlcDataValueType.Boolean:
                    return ((buffer[offset] >> bit) & 1) == 1;

                case PlcDataValueType.Byte:
                    return buffer[offset];

                case PlcDataValueType.Int:
                    return (short)((buffer[offset] << 8) | buffer[offset + 1]);

                case PlcDataValueType.Word:
                    return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

                case PlcDataValueType.DWord:
                    return ((uint)buffer[offset] << 24)
                        | ((uint)buffer[offset + 1] << 16)
                        | ((uint)buffer[offset + 2] << 8)
                        | buffer[offset + 3];

                case PlcDataValueType.Long:
                    return unchecked((int)(
                        ((uint)buffer[offset] << 24)
                        | ((uint)buffer[offset + 1] << 16)
                        | ((uint)buffer[offset + 2] << 8)
                        | buffer[offset + 3]));

                case PlcDataValueType.Float:
                    {
                        uint bits = ((uint)buffer[offset] << 24)
                            | ((uint)buffer[offset + 1] << 16)
                            | ((uint)buffer[offset + 2] << 8)
                            | buffer[offset + 3];
                        return BitConverter.UInt32BitsToSingle(bits);
                    }

                default:
                    return null;
            }
        }

        /// <summary>将数据写入 PLC。</summary>
        /// <param name="data">要写入的数据</param>
        public void WriteDataToPlc(S71200DataModel data)
        {
            if (!plc.IsConnected)
                throw new InvalidOperationException("S7-1200 PLC 未连接");

            try
            {
                // I（输入）区域为只读，不支持写入
                if (data.Area == S71200Area.Input)
                    throw new InvalidOperationException("输入点（I 区）只读，不支持写入");

                S7.Net.DataType s7DataType = ToS7DataType(data.Area);
                int db = GetDbNumber(data);

                if (data.ValueType == PlcDataValueType.Boolean)
                {
                    plc.WriteBit(s7DataType, db, data.StartByte, data.Bit, (bool)data.Value);
                }
                else if (data.ValueType == PlcDataValueType.Float)
                {
                    float realValue = Convert.ToSingle(data.Value);
                    plc.Write(s7DataType, db, data.StartByte, realValue);
                }
                else if (data.ValueType == PlcDataValueType.DWord)
                {
                    int dwordValue = Convert.ToInt32(data.Value);
                    plc.Write(s7DataType, db, data.StartByte, dwordValue);
                }
                else if (data.ValueType == PlcDataValueType.Long)
                {
                    int longValue = Convert.ToInt32(data.Value);
                    plc.Write(s7DataType, db, data.StartByte, longValue);
                }
                else if (data.ValueType == PlcDataValueType.Int)
                {
                    short intValue = Convert.ToInt16(data.Value);
                    plc.Write(s7DataType, db, data.StartByte, intValue);
                }
                else if (data.ValueType == PlcDataValueType.Word)
                {
                    ushort wordValue = Convert.ToUInt16(data.Value);
                    plc.Write(s7DataType, db, data.StartByte, wordValue);
                }
                else if (data.ValueType == PlcDataValueType.Byte)
                {
                    byte byteValue = Convert.ToByte(data.Value);
                    plc.Write(s7DataType, db, data.StartByte, byteValue);
                }

                Console.WriteLine($"成功写入：{data.Description}（{data.PlcAddress} = {data.Value}）");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入失败 {data.Description}：{ex.Message}");
                throw;
            }
        }

        /// <summary>按标签名写入指定值到 PLC。</summary>
        /// <param name="tag">变量名</param>
        /// <param name="value">变量值</param>
        public void WriteTag(string tag, object value)
        {
            if (PlcStatusList == null || PlcStatusList.Count == 0)
                throw new InvalidOperationException("S7-1200 PLC 点表未加载，无法写入");

            foreach (var info in PlcStatusList)
            {
                if (string.IsNullOrEmpty(info.TagName))
                    continue;

                if (string.Equals(tag, info.TagName, StringComparison.OrdinalIgnoreCase))
                {
                    info.Value = value;
                    WriteDataToPlc(info);
                    return;
                }
                if (IsExit) break;
            }

            throw new InvalidOperationException($"未找到 S7-1200 PLC 标签：{tag}");
        }

        /// <summary>停止后台读取线程但保持连接。</summary>
        public void Stop()
        {
            IsExit = true;
            if (WorkThread != null && WorkThread.IsAlive)
            {
                WorkThread.Join(2000);
            }
        }

        /// <summary>是否已释放资源。</summary>
        private bool _disposed;

        /// <summary>释放资源。</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>释放托管/非托管资源。</summary>
        /// <param name="disposing">是否释放托管资源</param>
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

            plc?.Close();

            _disposed = true;
        }

        /// <summary>析构函数。</summary>
        ~S71200()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// S7-1200 数据区域枚举。
    /// </summary>
    public enum S71200Area
    {
        /// <summary>输入区（I）。</summary>
        Input,
        /// <summary>输出区（Q）。</summary>
        Output,
        /// <summary>中间区（M）。</summary>
        Memory,
        /// <summary>数据块（DB）。</summary>
        DataBlock
    }

    /// <summary>
    /// S7-1200 PLC 数据模型（与 CSV 字段一一对应，并保存运行时值）。
    /// </summary>
    public class S71200DataModel
    {
        /// <summary>变量名称（对应 CSV 中的 Tag Name 字段）。</summary>
        public string TagName { get; set; }

        /// <summary>S7-1200 PLC 地址（如 DB47.DBD98、I0.0、Q0.1）。</summary>
        public string PlcAddress { get; set; }

        /// <summary>数据区域（I / Q / M / DB）。</summary>
        public S71200Area Area { get; set; }

        /// <summary>数据块编号（仅 DB 区域使用，如 DB47 的 47）。</summary>
        public int DbNumber { get; set; }

        /// <summary>起始字节（从 PlcAddress 解析，如 DBD98 的 98、I0.0 的 0）。</summary>
        public int StartByte { get; set; }

        /// <summary>位地址（按位读写时使用，如 DBX0.0 的 0、I0.0 的 0）。</summary>
        public int Bit { get; set; }

        /// <summary>数据类型（对应 CSV 中的 Data Type 字段）。</summary>
        public PlcDataValueType ValueType { get; set; }

        /// <summary>数据值（根据 ValueType 存储对应类型的值）。</summary>
        public object Value { get; set; }

        /// <summary>描述（对应 CSV 中的 Description 字段）。</summary>
        public string Description { get; set; }

        /// <summary>最后更新时间（自动填充）。</summary>
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// S7-1200 地址解析器，兼容 DB / I / Q / M 区域以及多种格式：
    /// DB47.DBD98、DB1001,DWORD1328、DB39.D68、DB47.DBX0.0、DB39.X0.0、I0.0、Q0.1 等。
    /// </summary>
    public static class S71200AddressParser
    {
        /// <summary>
        /// 解析地址，得到数据区域、数据块编号、起始字节和位地址。
        /// </summary>
        public static void Parse(string address, out S71200Area area, out int dbNumber, out int startByte, out int bit)
        {
            area = S71200Area.DataBlock;
            dbNumber = 0;
            startByte = 0;
            bit = 0;

            if (string.IsNullOrEmpty(address))
                throw new ArgumentException("S7-1200 地址不能为空");

            string s = address.Trim();

            // 判断区域前缀：DB 为数据块，I/Q/M 分别为输入/输出/中间区
            if (s.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
            {
                area = S71200Area.DataBlock;
                s = s.Substring(2);

                // 读取数据块编号
                int i = 0;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                if (i == 0)
                    throw new ArgumentException("S7-1200 地址缺少数据块编号：" + address);
                dbNumber = int.Parse(s.Substring(0, i));
                s = s.Substring(i);

                // 跳过分隔符（. 或 ,）
                if (s.Length > 0 && (s[0] == '.' || s[0] == ','))
                    s = s.Substring(1);
            }
            else if (s.StartsWith("I", StringComparison.OrdinalIgnoreCase))
            {
                area = S71200Area.Input;
                s = s.Substring(1);
            }
            else if (s.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
            {
                area = S71200Area.Output;
                s = s.Substring(1);
            }
            else if (s.StartsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                area = S71200Area.Memory;
                s = s.Substring(1);
            }
            else
            {
                throw new ArgumentException("不支持的 S7-1200 地址：" + address);
            }

            // 读取类型前缀（DBD/DWORD/D/DBW/W/DBX/X/DBB/B 等）
            int j = 0;
            while (j < s.Length && char.IsLetter(s[j])) j++;
            string typePrefix = s.Substring(0, j).ToUpperInvariant();
            s = s.Substring(j);

            // 读取字节编号
            int k = 0;
            while (k < s.Length && char.IsDigit(s[k])) k++;
            if (k == 0)
                throw new ArgumentException("S7-1200 地址缺少偏移量：" + address);
            startByte = int.Parse(s.Substring(0, k));
            s = s.Substring(k);

            // 可选的位地址（如 DBX0.0、I0.0）
            if (s.StartsWith("."))
            {
                if (!int.TryParse(s.Substring(1), out bit))
                    throw new ArgumentException("S7-1200 位地址错误：" + address);
            }
        }
    }

    /// <summary>
    /// S7-1200 CSV 字段映射配置（Tag Name / Address / Data Type / Description）。
    /// </summary>
    public class S71200DataModelMap : ClassMap<S71200DataModel>
    {
        public S71200DataModelMap()
        {
            // 引用类型字段直接映射
            Map(m => m.TagName).Name("Tag Name");
            Map(m => m.PlcAddress).Name("Address");
            Map(m => m.Description).Name("Description");

            // 数据类型（Data Type → PlcDataValueType）
            Map(m => m.ValueType)
                .Name("Data Type")
                .Convert(args =>
                {
                    string dataType = args.Row.GetField<string>("Data Type").ToLower();
                    return dataType switch
                    {
                        "boolean" or "布尔" => PlcDataValueType.Boolean,
                        "byte" or "字节" => PlcDataValueType.Byte,
                        "int" or "整型" => PlcDataValueType.Int,
                        "word" or "字" => PlcDataValueType.Word,
                        "dword" or "双整型" or "双字" => PlcDataValueType.DWord,
                        "long" or "长整型" => PlcDataValueType.Long,
                        "float" or "浮点" or "浮点型" => PlcDataValueType.Float,
                        _ => throw new ArgumentException($"不支持的数据类型：{dataType}（行，变量{args.Row.GetField<string>("Tag Name")}）")
                    };
                });

            // 数据区域（从 Address 解析）
            Map(m => m.Area)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    S71200AddressParser.Parse(address, out S71200Area area, out int dbNumber, out int startByte, out int bit);
                    return area;
                });

            // 数据块编号（从 Address 解析）
            Map(m => m.DbNumber)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    S71200AddressParser.Parse(address, out S71200Area area, out int dbNumber, out int startByte, out int bit);
                    return dbNumber;
                });

            // 起始字节（从 Address 解析）
            Map(m => m.StartByte)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    S71200AddressParser.Parse(address, out S71200Area area, out int dbNumber, out int startByte, out int bit);
                    return startByte;
                });

            // 位地址（从 Address 解析）
            Map(m => m.Bit)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    S71200AddressParser.Parse(address, out S71200Area area, out int dbNumber, out int startByte, out int bit);
                    return bit;
                });

            // 数据值不参与 CSV 映射
            Map(m => m.Value).Ignore();

            // 更新时间自动填充
            Map(m => m.UpdateTime).Convert(args => DateTime.Now);
        }
    }
}
