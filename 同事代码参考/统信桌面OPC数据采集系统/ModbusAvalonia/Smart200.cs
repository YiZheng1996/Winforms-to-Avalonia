//Install-Package CsvHelper -Version 33.0.1
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using S7.Net;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Threading;

using System.Xml.Linq;


using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
//data.Value = plc.Read(S7.Net.DataType.DataBlock, data.DbNumber, data.StartByte, VarType.Bit, 1);
// plc.WriteBit(S7.Net.DataType.DataBlock, data.DbNumber, data.StartByte, data.Bit, (bool)data.Value);
namespace PressureTestApp.Communication.Plc
{ 
    /// <summary>
    /// PLC 通信通用接口
    /// </summary>
    public interface IPlc200Communicator : IDisposable
    {
        /// <summary>
        /// PLC 连接状态
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 停止后台线程但保持连接（用于退出登录时）
        /// </summary>
        void Stop();

        /// <summary>
        /// PLC 连接结果信息
        /// </summary>
        string ConnectionMessage { get; }

        /// <summary>
        /// 从 CSV 映射 PLC 变量并启动实时读取
        /// </summary>
        /// <param name="csvPath">CSV 文件路径</param>
        void MappingPlcFromCSV(string csvPath);

        /// <summary>
        /// 写入指定 Tag 到 PLC
        /// </summary>
        /// <param name="tagName">变量名</param>
        /// <param name="value">变量值</param>
        void WriteTag(string tagName, object value);

        /// <summary>
        /// 获取 PLC 实时数据列表
        /// </summary>
        List<PlcDataModel> GetPlcStatusList();

        /// <summary>
        /// 断开 PLC 连接
        /// </summary>
        void Disconnect();
    }
    /// <summary>
    /// S7-200 SMART PLC 通信实现（基于 S7netplus）
    /// </summary>
    public class Smart200 : IPlc200Communicator
    {
        /// <summary>
        /// PLC 连接状态
        /// </summary>
        public bool IsConnected => plc?.IsConnected ?? false;

        /// <summary>
        /// 连接结果信息
        /// </summary>
        public string ConnectionMessage => conStr;

        /// <summary>
        /// 获取 PLC 实时数据列表
        /// </summary>
        public List<PlcDataModel> GetPlcStatusList() => PlcStatusList;

        /// <summary>
        /// 断开 PLC 连接
        /// </summary>
        public void Disconnect()
        {
            IsExit = true; // 停止读取线程
            WorkThread?.Join(1000); // 等待线程退出
            plc?.Close(); // 关闭 PLC 连接
        }

        /// <summary>
        /// S7netplus PLC 连接对象
        /// </summary>
        public S7.Net.Plc plc;

        /// <summary>
        /// PLC 实时数据列表
        /// </summary>
        public List<PlcDataModel> PlcStatusList;

        /// <summary>
        /// 连接结果信息
        /// </summary>
        public string conStr = "";

        /// <summary>
        /// 构造函数：创建 PLC 对象并尝试建立连接
        /// </summary>
        /// <param name="ip">PLC 的 IP 地址</param>
        public Smart200(string ip)
        {

            plc = new S7.Net.Plc(
                CpuType.S7200Smart,    // PLC型号（关键！根据实际PLC修改）
                ip.ToString(),        //"192.168.0.11",   // PLC的IP地址（和电脑同网段）
                0,                   // Rack号（S7-1200/200SMART固定为0）
                1                   // Slot号（固定为1）
            );

            try
            {
                // ========== 2. 建立连接 ==========
                Console.WriteLine("正在连接PLC...");

                plc.Open();

                if (plc.IsConnected)
                {
                    Console.WriteLine("✅ PLC连接成功！");
                    conStr = "✅ PLC连接成功！";
                    //listBox1.Items.Add("✅ PLC连接成功！");

                }
                else
                {
                    Console.WriteLine("❌ PLC连接失败！");
                    conStr = "❌ PLC连接失败！";
                    //listBox1.Items.Add("❌ PLC连接失败！");
                }
            }
            catch (Exception ex)
            {
                // 捕获通信异常（如IP错误、PLC离线、地址不存在）
                Console.WriteLine($"❌ 通信出错：{ex.Message}");
                conStr = "❌ 通信出错：{ex.Message}";
                //listBox1.Items.Add("❌ 通信出错：{ex.Message}");
            }


        }
        /// <summary>
        /// 后台读取线程
        /// </summary>
        Thread WorkThread = null;

        /// <summary>
        /// 从 CSV 解析出的点表
        /// </summary>
        List<PlcDataModel> plcDataList;

        /// <summary>
        /// 是否停止后台读取线程
        /// </summary>
        public bool IsExit = false;

        /// <summary>
        /// 从 CSV 映射 PLC 变量并启动后台实时读取
        /// </summary>
        /// <param name="path">CSV 文件路径</param>
        public void MappingPlcFromCSV(string path)
        {
            if (!plc.IsConnected)
                throw new InvalidOperationException("PLC 未连接");

            // 1. 解析 CSV 文件（替换为你的文件路径）
            plcDataList = ParseCsv(path);
            // 初值用点列表快照（非 null），实际值由后台线程异步刷新，避免同步读取阻塞加载
            PlcStatusList = new List<PlcDataModel>(plcDataList);
            IsExit = false;
            WorkThread = new Thread(ReadFromPLC) { IsBackground = true };
            if (WorkThread != null)
                WorkThread?.Start();
        }

        /// <summary>
        /// 后台读取线程主体，循环读取 PLC 数据
        /// </summary>
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

        /// <summary>
        /// 解析 CSV 文件（按 Tag Name/Address/Data Type/Description 字段）
        /// </summary>
        /// <param name="filePath">CSV 文件路径</param>
        /// <returns>解析后的 PLC 数据列表</returns>
        public List<PlcDataModel> ParseCsv(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("CSV 文件不存在", filePath);

            // .NET 8 + CsvHelper 33.0.1 配置（修复 Row 访问路径）
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true, // 首行为表头
                Delimiter = ",",        // CSV 分隔符（若为其他符号，需改为 ; 或 \t）
                Encoding = System.Text.Encoding.UTF8, // 支持中文
                MissingFieldFound = args =>
                {
                    // 缺失必填字段时报错
                    throw new Exception(
                        $"缺失必填字段：{args.HeaderNames.First()}（行{args.Context.Parser.Row}）");
                },
                BadDataFound = args =>
                {
                    // 数据格式错误时提示（修复 Context.Parser.Row 路径）
                    Console.WriteLine($"警告：行 {args.Context.Parser.Row} 数据格式错误，原始数据：{args.RawRecord}");
                }
            };

            using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8);
            using var csv = new CsvReader(reader, config, leaveOpen: false);

            // 注册新的映射配置（关键：使用重新设计的 PlcDataModelMap）
            csv.Context.RegisterClassMap<PlcDataModelMap>();

            try
            {
                var plcDataList = csv.GetRecords<PlcDataModel>().ToList();
                // 输出解析结果（验证映射是否正确）
                Console.WriteLine($"=== CSV 解析完成 ===");
                Console.WriteLine($"总解析条数：{plcDataList.Count} 条");
                Console.WriteLine($"示例数据（第一条）：");
                Console.WriteLine($"  变量名称：{plcDataList[0].TagName}");
                Console.WriteLine($"  PLC 地址：{plcDataList[0].PlcAddress}");
                Console.WriteLine($"  数据类型：{plcDataList[0].ValueType}");
                Console.WriteLine($"  解析后区域：{plcDataList[0].DataArea}（字节：{plcDataList[0].StartByte}，位：{plcDataList[0].Bit}）");

                return plcDataList;
            }
            catch (Exception ex)
            {
                throw new Exception($"CSV 解析失败：{ex.Message}", ex);
            }
        }
        /// <summary>
        /// 从PLC读数据
        /// </summary>
        /// <param name="plcDataList">解析后的 PLC 数据列表</param>
        public List<PlcDataModel> ReadDataFromPlc(List<PlcDataModel> plcDataList)
        {
            foreach (var data in plcDataList)
            {
                try
                {
                    // 转换数据区域为 S7NetPlus 对应的 DataType
                    var s7DataType = data.DataArea switch
                    {
                        PlcDataType.Input => S7.Net.DataType.Input,
                        PlcDataType.Output => S7.Net.DataType.Output,
                        PlcDataType.Memory => S7.Net.DataType.Memory,
                        PlcDataType.Variable => S7.Net.DataType.DataBlock, // V区对应 DataBlock 1
                        _ => throw new ArgumentException($"不支持的数据区域：{data.DataArea}")
                    };

                    // 写入数据（区分位数据和字节/字数据）
                    if (s7DataType == S7.Net.DataType.Memory) //.ValueType == PlcDataValueType.Boolean)
                    {
                        object intBytes = "";
                        intBytes = plc.Read(
                            s7DataType,  // ① 数据类型（DB块 ）M点默认DB块编号为0   DataType.Memory->M点
                            0,            // ② DB号 M点默认DB块编号为0
                            data.StartByte,                  // ③ 起始字节地址（M0）
                            VarType.Bit,       // ④ 变量类型（BOOL对应BIT）
                            data.Bit + 1,                   // ⑤ varCount：变量数量（读1个BOOL）
                            0                    // ⑥ 数据块长度（旧版传0即可）
                        );
                        bool bit = false;
                        if (intBytes is BitArray bits)
                        {
                            // 方法1：遍历所有位
                            for (int i = 0; i < bits.Length; i++)
                            {
                                bool value = bits[i];
                                //Console.WriteLine($"V0.{i} = {value}");
                            }

                            // 方法2：获取特定位（如 V0.3）
                            bit = bits[data.Bit];
                        }
                        else
                        {
                            bit = Convert.ToBoolean(intBytes);
                        }
                        if (intBytes != null)
                        {
                            data.Value = bit;
                        }
                    }
                    if (data.ValueType == PlcDataValueType.Float)
                    {
                        object intBytes = "";
                        intBytes = plc.Read(
                            S7.Net.DataType.DataBlock,  // ① 数据类型（DB块）
                            1,            // ② DB号
                            data.StartByte,                  // ③ 起始字节地址（DB1.DBW10）
                            VarType.Real,       // ④ 变量类型（INT对应Int16）
                            1,                   // ⑤ varCount：变量数量（读1个INT）
                            0                    // ⑥ 数据块长度（旧版传0即可）
                        );
                        if (intBytes != null)
                        {
                            data.Value = intBytes;
                        }
                    }
                    if (data.ValueType == PlcDataValueType.DWord)
                    {
                        // 读双字数据（如 VD1000）

                        object dwordBytes = "";
                        dwordBytes = plc.Read(
                            s7DataType,  // ① 数据类型（DB块 ）V点默认DB块编号为1   DataType.Memory->M点
                            1,            // ② DB号 V点默认DB块编号为1
                            data.StartByte,                  // ③ 起始字节地址（V0）
                            VarType.DWord,       // ④ 变量类型（DWord对应Int32）
                            1,                   // ⑤ varCount：变量数量（读1个DWord）
                            0                    // ⑥ 数据块长度（旧版传0即可）
                        );
                        if (dwordBytes != null)
                        {
                            data.Value = dwordBytes;
                        }
                    }
                    if (s7DataType == S7.Net.DataType.DataBlock && data.ValueType == PlcDataValueType.Boolean)
                    {
                        // 读字节数据（如 VB100）
                        //data.Bit++;
                        object intBytes = "";
                        intBytes = plc.Read(
                            s7DataType,  // ① 数据类型（DB块 ）V点默认DB块编号为1   DataType.Memory->M点
                            1,            // ② DB号 V点默认DB块编号为1
                            data.StartByte,                  // ③ 起始字节地址（V0）
                            VarType.Bit,       // ④ 变量类型（INT对应Int16）
                            data.Bit + 1,                   // ⑤ varCount：变量数量（读1个INT）
                            0                    // ⑥ 数据块长度（旧版传0即可）
                        );

                        bool bit = false;
                        if (intBytes is BitArray bits)
                        {
                            // 方法1：遍历所有位
                            for (int i = 0; i < bits.Length; i++)
                            {
                                bool value = bits[i];
                                //Console.WriteLine($"V0.{i} = {value}");
                            }

                            // 方法2：获取特定位（如 V0.3）
                            bit = bits[data.Bit];
                        }
                        else
                        {
                            bit = Convert.ToBoolean(intBytes);
                        }
                        if (intBytes != null)
                        {
                            data.Value = bit;
                        }
                    }

                    //Console.WriteLine($"成功读取：{data.Description}（{data.DataArea}{data.StartByte}.{data.Bit} = {data.Value}）");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"读取失败 {data.Description}：{ex.Message}");
                }

            }
            return plcDataList;
        }

        /// <summary>
        /// 将解析后的 CSV 数据写入 PLC
        /// </summary>
        /// <param name="plcDataList">解析后的 PLC 数据列表</param>
        public void WriteDataToPlc(PlcDataModel plcDataList)
        {
            if (!plc.IsConnected)
                throw new InvalidOperationException("PLC 未连接");
            var data = plcDataList;
            //foreach (var data in plcDataList)
            {
                try
                {
                    // 转换数据区域为 S7NetPlus 对应的 DataType
                    var s7DataType = data.DataArea switch
                    {
                        PlcDataType.Input => S7.Net.DataType.Input,
                        PlcDataType.Output => S7.Net.DataType.Output,
                        PlcDataType.Memory => S7.Net.DataType.Memory,
                        PlcDataType.Variable => S7.Net.DataType.DataBlock, // V区对应 DataBlock 1
                        _ => throw new ArgumentException($"不支持的数据区域：{data.DataArea}")
                    };


                     // 写入数据（区分位数据和字节/字数据）
                    if (s7DataType == S7.Net.DataType.Memory) //.ValueType == PlcDataValueType.Boolean)
                    {
                        object intBytes = "";
                        plc.WriteBit(
                            s7DataType,  // ① 数据类型（DB块 ）M点默认DB块编号为0   DataType.Memory->M点
                            0,            // ② DB号 M点默认DB块编号为0
                            data.StartByte,                  // ③ 起始字节地址（M0）
                            data.Bit,       // ④ 变量类型（BOOL对应BIT）
                            (bool)data.Value                  // ⑤ varCount：变量数量（读1个BOOL）

                        );
                    }

                    if (data.ValueType == PlcDataValueType.Float)
                    {
                        float realValue = Convert.ToSingle(data.Value);

                        plc.Write(
                            S7.Net.DataType.DataBlock,  // ① 数据类型（DB块）
                            1,            // ② DB号
                            data.StartByte,                  // ③ 起始字节地址（DB1.DBD500）
                            realValue
                        );
                    }
                    else if (data.ValueType == PlcDataValueType.DWord)
                    {
                        int dwordValue = Convert.ToInt32(data.Value);

                        plc.Write(
                            S7.Net.DataType.DataBlock,  // ① 数据类型（DB块）
                            1,            // ② DB号
                            data.StartByte,                  // ③ 起始字节地址（DB1.DBD1000）
                            dwordValue
                        );
                    }
                    else if (data.Value is bool boolValue)
                    {
                        // 写入位数据（如 Q0.1）
                        plc.WriteBit(s7DataType,
                                     data.DataArea == PlcDataType.Variable ? 1 : 0, // V区对应 DB1
                                     data.StartByte,
                                     data.Bit,
                                     boolValue);
                    }
                    else if (int.TryParse(data.Value.ToString(), out int intValue))
                    {
                        // 写入字节数据（如 VB100）
                        plc.Write(s7DataType,
                                     data.DataArea == PlcDataType.Variable ? 1 : 0,
                                     data.StartByte,
                                     (byte)intValue);
                    }

                    Console.WriteLine($"成功写入：{data.Description}（{data.DataArea}{data.StartByte}.{data.Bit} = {data.Value}）");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"写入失败 {data.Description}：{ex.Message}");
                    throw; // 让上层 WritePlcTag 能提示「值没给到仪器」
                }
            }
        }

        /// <summary>
        /// 按标签名写入指定值到 PLC
        /// </summary>
        /// <param name="tag">变量名</param>
        /// <param name="value">变量值</param>
        public void WriteTag(string tag, object value)
        {
            List<PlcDataModel> WorkPlc = PlcStatusList;
            if (WorkPlc == null || WorkPlc.Count == 0)
                throw new InvalidOperationException("PLC 点表未加载，无法写入");

            foreach (PlcDataModel info in WorkPlc)
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

            throw new InvalidOperationException($"未找到 PLC 标签：{tag}");
        }

        /// <summary>
        /// 停止后台读取线程但保持连接
        /// </summary>
        public void Stop()
        {
            IsExit = true;
            if (WorkThread != null && WorkThread.IsAlive)
            {
                WorkThread.Join(2000);
            }
        }

        /// <summary>
        /// 是否已释放资源
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放托管/非托管资源
        /// </summary>
        /// <param name="disposing">是否释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // 停止后台工作线程
                IsExit = true;
                if (WorkThread != null && WorkThread.IsAlive)
                {
                    WorkThread.Join(2000);
                }
            }

            // 关闭 PLC 连接
            plc?.Close();
            //plc?.Dispose();

            _disposed = true;
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~Smart200()
        {
            Dispose(false);
        }

    }

    /// <summary>
    /// PLC 数据区域枚举（S7-200 SMART 支持的 I/Q/M/V 区）
    /// </summary>
    public enum PlcDataType
    {
        [Description("输入寄存器（I区）")]
        Input,
        [Description("输出寄存器（Q区）")]
        Output,
        [Description("中间寄存器（M区）")]
        Memory,
        [Description("变量寄存器（V区）")]
        Variable
    }

    /// <summary>
    /// PLC 数据值类型枚举（对应 CSV 中的 Data Type 字段）
    /// </summary>
    public enum PlcDataValueType
    {
        [Description("布尔型（位数据）")]
        Boolean,
        [Description("字节型（8位）")]
        Byte,
        [Description("整型（16位）")]
        Int,
        [Description("字（16位无符号）")]
        Word,
        [Description("双整型（32位）")]
        DWord,
        [Description("长整型（32位有符号）")]
        Long,
        [Description("浮点型（32位）")]
        Float
    }

    /// <summary>
    /// S7-200 SMART PLC 数据模型（与 CSV 字段一一对应）
    /// </summary>
    public class PlcDataModel
    {
        /// <summary>
        /// 变量名称（对应 CSV 中的 Tag Name 字段）
        /// </summary>
        [Required(ErrorMessage = "变量名称（Tag Name）不可缺失")]
        public string TagName { get; set; }

        /// <summary>
        /// SMART 200 PLC 地址（对应 CSV 中的 Address 字段，如 Q0.1、M10.3、V20.0）
        /// </summary>
        [Required(ErrorMessage = "PLC地址（Address）不可缺失")]
        public string PlcAddress { get; set; }

        /// <summary>
        /// 数据区域类型（I/Q/M/V，从 PlcAddress 解析，无需 CSV 单独提供）
        /// </summary>
        public PlcDataType DataArea { get; set; }

        /// <summary>
        /// 起始字节（从 PlcAddress 解析，如 Q0.1 解析为 0）
        /// </summary>
        public int StartByte { get; set; }

        /// <summary>
        /// 位地址（从 PlcAddress 解析，如 Q0.1 解析为 1；字节/字数据为 0）
        /// </summary>
        public int Bit { get; set; }

        /// <summary>
        /// 数据类型（对应 CSV 中的 Data Type 字段，如 Bool、Byte、Int）
        /// </summary>
        [Required(ErrorMessage = "数据类型（Data Type）不可缺失")]
        public PlcDataValueType ValueType { get; set; }

        /// <summary>
        /// 数据值（根据 ValueType 存储对应类型的值）
        /// </summary>
        public object Value { get; set; }

        /// <summary>
        /// 描述（对应 CSV 中的 Description 字段）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 最后更新时间（自动填充，无需 CSV 提供）
        /// </summary>
        public DateTime UpdateTime { get; set; }
    }

    /// <summary>
    /// CSV 字段映射配置（严格对应：Tag Name/Address/Data Type/Description）
    /// </summary>
    public class PlcDataModelMap : ClassMap<PlcDataModel>
    {
        public PlcDataModelMap()
        {
            #region 1. 引用类型映射（string，直接 Map + Name）
            Map(m => m.TagName).Name("Tag Name");
            Map(m => m.PlcAddress).Name("Address");
            Map(m => m.Description).Name("Description");
            #endregion


            #region 2. 值类型映射（枚举/int，用 Convert + Func<ICsvReaderRow, TMember>）
            // 数据类型（Data Type → PlcDataValueType，枚举值类型）
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
                        "dword" or "双整型" => PlcDataValueType.DWord,
                        "float" or "浮点型" => PlcDataValueType.Float,

                        _ => throw new ArgumentException(
                                                  $"不支持的数据类型：{dataType}（行，变量{args.Row.GetField<string>("Tag Name")}）")

                    };
                });

            // 描述 → Description（CSV 表头：Description）
            Map(m => m.Description)
                .Name("Description")
                .Optional(); // 描述为可选字段，缺失不报错
            #endregion

            #region 3. 从 Address 字段解析衍生字段（无需 CSV 提供）
            // 数据区域（从 Address 首字符解析：I→Input，Q→Output，M→Memory，V→Variable）
            Map(m => m.DataArea)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address").TrimStart();
                    char areaChar = address[0]; // 取地址首字符（如 Q0.1 的 Q）
                    return areaChar switch
                    {
                        'I' or 'i' => PlcDataType.Input,
                        'Q' or 'q' => PlcDataType.Output,
                        'M' or 'm' => PlcDataType.Memory,
                        'V' or 'v' => PlcDataType.Variable,
                        _ => throw new ArgumentException(
                            $"不支持的地址类型：{areaChar}（行，地址{address}）")
                    };
                });

            // 起始字节（从 Address 解析，如 Q0.1→0，V20.5→20）
            Map(m => m.StartByte)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    string numPart = "";
                    // 提取地址中的数字部分（去掉首字符和可能的位分隔符 .）
                    if (address.IndexOf("D") > 0)
                    {
                        numPart = new string(
                           address.Skip(2).TakeWhile(c => c != '.').ToArray());
                    }
                    else
                    {
                        numPart = new string(
                           address.Skip(1).TakeWhile(c => c != '.').ToArray()
                       );
                    }
                    if (!int.TryParse(numPart, out int startByte))
                        throw new ArgumentException(
                            $"地址格式错误，无法解析起始字节：{address}（行，变量{args.Row.GetField<string>("Tag Name")}）");
                    return startByte;
                });

            // 位地址（从 Address 解析，有 . 则取后面数字，无则为 0）
            Map(m => m.Bit)
                .Convert(args =>
                {
                    string address = args.Row.GetField<string>("Address");
                    if (!address.Contains('.'))
                        return 0; // 字节/字数据，位地址默认为 0

                    // 提取 . 后面的数字（如 Q0.1→1，M10.3→3）
                    string bitPart = address.Split('.')[1];
                    if (!int.TryParse(bitPart, out int bit) || bit < 0 || bit > 7)
                        throw new ArgumentException(
                            $"位地址错误（需为 0-7）：{address}（行，变量{args.Row.GetField<string>("Tag Name")}）");
                    return bit;
                });
            #endregion

            #region 3. 自动填充字段（无需 CSV 提供）
            // 数据值（默认初始化为空，后续可从 PLC 读取或手动赋值）
            Map(m => m.Value)
                .Ignore(); // 忽略 CSV 映射，后续通过业务逻辑赋值

            // 最后更新时间（自动填充当前时间）
            Map(m => m.UpdateTime)
                .Convert(args => DateTime.Now);
            #endregion
        }
    }
    /// <summary>
    /// PLC 配置类（对应 appsettings.json 中的 Plc200Config 节点）
    /// </summary>
    public class Plc200Config
    {
        /// <summary>
        /// 是否启用 PLC 通信
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Smart200 PLC 的 IP 地址
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// PLC 变量映射 CSV 文件路径
        /// </summary>
        public string CsvFilePath { get; set; }
    }

    /// <summary>
    /// Mock PLC 通信实现（用于测试/禁用 PLC 时提供模拟数据）
    /// </summary>
    public class MockPlc200Communicator : IPlc200Communicator
    {
        private readonly List<PlcDataModel> _mockData = new();
        private readonly Random _random = new();
        private bool _initialized;

        public bool IsConnected => false;
        public string ConnectionMessage => "PLC 未连接（使用模拟数据）";

        public void MappingPlcFromCSV(string csvPath)
        {
            if (_initialized || !File.Exists(csvPath))
                return;

            try
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    Delimiter = ",",
                    Encoding = System.Text.Encoding.UTF8,
                    MissingFieldFound = args => { }
                };

                using var reader = new StreamReader(csvPath, System.Text.Encoding.UTF8);
                using var csv = new CsvReader(reader, config, leaveOpen: false);
                csv.Context.RegisterClassMap<PlcDataModelMap>();

                var plcDataList = csv.GetRecords<PlcDataModel>().ToList();
                
                foreach (var data in plcDataList)
                {
                    var mockItem = new PlcDataModel
                    {
                        TagName = data.TagName,
                        PlcAddress = data.PlcAddress,
                        DataArea = data.DataArea,
                        StartByte = data.StartByte,
                        Bit = data.Bit,
                        ValueType = data.ValueType,
                        Description = data.Description,
                        Value = GenerateMockValue(data),
                        UpdateTime = DateTime.Now
                    };
                    _mockData.Add(mockItem);
                }

                _initialized = true;
                Console.WriteLine($"✅ Mock PLC 模拟数据已加载：{_mockData.Count} 个变量");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Mock PLC 初始化失败：{ex.Message}");
            }
        }

        private object GenerateMockValue(PlcDataModel data)
        {
            return data.ValueType switch
            {
                PlcDataValueType.Boolean => _random.NextDouble() > 0.3,
                PlcDataValueType.Byte => (byte)_random.Next(256),
                PlcDataValueType.Int => (short)_random.Next(-32768, 32767),
                PlcDataValueType.DWord => _random.Next(int.MinValue, int.MaxValue),
                PlcDataValueType.Float => GenerateMockFloat(data.TagName),
                _ => 0
            };
        }

        private float GenerateMockFloat(string tagName)
        {
            var tag = tagName.ToUpper();
            
            if (tag.Contains("MAI00") || tag.Contains("HIGHVOLTAGE") || tag.Contains("VOLTAGE"))
                return (float)(_random.NextDouble() * 500 + 1000);
            
            if (tag.Contains("MAI01") || tag.Contains("LEAKAGE") || tag.Contains("CURRENT"))
                return (float)(_random.NextDouble() * 10 + 0.5);
            
            if (tag.Contains("MAI02") || tag.Contains("DISCHARGE"))
                return (float)(_random.NextDouble() * 100 + 10);
            
            if (tag.Contains("MAI03") || tag.Contains("FACTORY"))
                return (float)(_random.NextDouble() * 20 + 210);
            
            if (tag.Contains("P00") || tag.Contains("SETVOLTAGE"))
                return (float)(_random.NextDouble() * 5000 + 10000);
            
            if (tag.Contains("P01") || tag.Contains("PROTECTMA"))
                return (float)(_random.NextDouble() * 100 + 50);
            
            if (tag.Contains("P02") || tag.Contains("DURATION"))
                return (float)(_random.NextDouble() * 60 + 30);

            return (float)(_random.NextDouble() * 1000);
        }

        public void WriteTag(string tagName, object value)
        {
            if (_mockData.Count == 0)
                throw new InvalidOperationException("Mock PLC 点表未加载，请配置 CsvFilePath");

            var item = _mockData.FirstOrDefault(d => string.Equals(d.TagName, tagName, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                throw new InvalidOperationException($"未找到 PLC 标签：{tagName}");

            item.Value = value;
            item.UpdateTime = DateTime.Now;
        }

        public List<PlcDataModel> GetPlcStatusList()
        {
            UpdateMockValues();
            return _mockData.ToList();
        }

        private void UpdateMockValues()
        {
            foreach (var item in _mockData)
            {
                // 设定值/启动位不要随机漂移，否则刚下发的电压电流会被冲掉
                var tag = item.TagName ?? "";
                if (tag.StartsWith("PUB.P0", StringComparison.OrdinalIgnoreCase) ||
                    tag.Equals("PUB.P61", StringComparison.OrdinalIgnoreCase) ||
                    tag.Equals("PUB.P62", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (item.ValueType == PlcDataValueType.Float)
                {
                    var current = Convert.ToSingle(item.Value);
                    var variation = (float)(_random.NextDouble() - 0.5) * current * 0.02;
                    item.Value = Math.Max(0, current + variation);
                }
                else if (item.ValueType == PlcDataValueType.Boolean && _random.NextDouble() > 0.95)
                {
                    item.Value = !(bool)item.Value;
                }
                
                item.UpdateTime = DateTime.Now;
            }
        }

        public void Disconnect()
        {
            _mockData.Clear();
            _initialized = false;
        }

        public void Stop()
        {
            // Mock 实现，无需特殊处理
        }

        public void Dispose()
        {
            _mockData.Clear();
            _initialized = false;
        }
    }
}
