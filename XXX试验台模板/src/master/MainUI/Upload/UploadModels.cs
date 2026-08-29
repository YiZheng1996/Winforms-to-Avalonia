using FreeSql.DataAnnotations;
using Newtonsoft.Json;

namespace MainUI.Upload
{
    /// <summary>
    /// “上传试验数据”接口的请求体，对应 /api/report/AddProcessRecord。
    /// JsonProperty 用于固定接口字段名，避免 C# 属性命名变化影响接口报文。
    /// </summary>
    internal sealed class ProcessRecordRequest
    {
        /// <summary>设备名称，来自 UploadConfig.ini。</summary>
        [JsonProperty("deviceName")]
        public string DeviceName { get; set; }

        /// <summary>设备编码，来自 UploadConfig.ini。</summary>
        [JsonProperty("deviceCode")]
        public string DeviceCode { get; set; }

        /// <summary>
        /// 产品序列号。当前按界面已有制造编号原样上传，不在客户端校验或转换。
        /// </summary>
        [JsonProperty("productSeqNo")]
        public string ProductSeqNo { get; set; }

        /// <summary>本次试验备注，来自当前产品选择信息。</summary>
        [JsonProperty("remark")]
        public string Remark { get; set; }

        /// <summary>本次试验的结构化试验项集合。</summary>
        [JsonProperty("testData")]
        public List<TestDataItem> TestData { get; set; } = [];
    }

    /// <summary>
    /// 单个试验项上传模型，对应接口 testData 数组中的一个元素。
    /// </summary>
    internal sealed class TestDataItem
    {
        /// <summary>界面/配置中的中文试验项名称。</summary>
        [JsonProperty("testItem")]
        public string TestItem { get; set; }

        /// <summary>
        /// 试验值。接口要求为字符串；一个试验项包含多个报表值时，使用 JSON 对象字符串保存。
        /// </summary>
        [JsonProperty("value")]
        public string Value { get; set; }

        /// <summary>
        /// 单位。现有试验类没有统一的单位元数据，当前默认上传空字符串，后续可按试验项补充。
        /// </summary>
        [JsonProperty("unit")]
        public string Unit { get; set; }

        /// <summary>试验项执行结果：true 为合格/成功，false 为不合格/失败。</summary>
        [JsonProperty("isOk")]
        public bool IsOk { get; set; }
    }

    /// <summary>
    /// 后端统一响应包装模型。客户端同时检查 HTTP 状态码和业务 code。
    /// </summary>
    /// <typeparam name="T">响应 data 字段对应的数据类型。</typeparam>
    internal sealed class ApiResponse<T>
    {
        /// <summary>业务状态码，当前约定 0 表示成功。</summary>
        [JsonProperty("code")]
        public int Code { get; set; }

        /// <summary>接口返回的业务数据。</summary>
        [JsonProperty("data")]
        public T Data { get; set; }

        /// <summary>接口返回的错误说明。</summary>
        [JsonProperty("error")]
        public string Error { get; set; }

        /// <summary>接口返回的提示信息。</summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>服务端生成响应的时间。</summary>
        [JsonProperty("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }
    }

    /// <summary>
    /// 文件上传成功后 data 字段中当前会使用到的内容。
    /// XLS 不与试验数据关联，但保留文件 ID 便于日志和后续扩展。
    /// </summary>
    internal sealed class FileUploadResult
    {
        /// <summary>服务端生成的文件记录 ID。</summary>
        [JsonProperty("id")]
        public Guid ID { get; set; }

        /// <summary>服务端返回的文件访问地址。</summary>
        [JsonProperty("fileUrl")]
        public string FileUrl { get; set; }
    }

    /// <summary>
    /// 本地 SQLite 上传任务表。
    /// 每次保存试验报告时生成一条记录；试验数据和 XLS 文件分别记录上传结果，
    /// 从而保证人工点击“重新上传”时只提交尚未成功的部分。
    /// </summary>
    [Table(Name = "UploadTask")]
    internal sealed class UploadTaskModel
    {
        /// <summary>本地上传任务自增主键。</summary>
        [Column(IsPrimary = true, IsIdentity = true)]
        public long ID { get; set; }

        /// <summary>对应本地 Record 表中的试验记录 ID。</summary>
        public int RecordID { get; set; }

        /// <summary>
        /// 创建任务时固化的试验数据 JSON 快照。
        /// 人工补传必须使用该快照，不能重新读取可能已经变化的界面全局变量。
        /// </summary>
        [Column(DbType = "TEXT")]
        public string PayloadJson { get; set; }

        /// <summary>待上传 XLS 报表的完整本地路径。</summary>
        [Column(StringLength = 1000)]
        public string ReportPath { get; set; }

        /// <summary>试验数据接口是否已经明确返回成功。</summary>
        [Column(MapType = typeof(bool))]
        public bool DataUploaded { get; set; }

        /// <summary>文件上传接口是否已经明确返回成功。</summary>
        [Column(MapType = typeof(bool))]
        public bool FileUploaded { get; set; }

        /// <summary>
        /// 兼容已创建数据库表的保留字段。简化方案不再累计失败次数，也不执行自动退避重试。
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// 兼容已创建数据库表的保留字段。简化方案不再按时间自动扫描或重试任务。
        /// </summary>
        public DateTime NextRetryTime { get; set; }

        /// <summary>最近一次上传失败的错误信息，供日志和数据管理页面查看。</summary>
        [Column(DbType = "TEXT")]
        public string LastError { get; set; }

        /// <summary>文件接口返回的文件 ID；接口未返回时允许为空。</summary>
        [Column(StringLength = 100)]
        public string ServerFileID { get; set; }

        /// <summary>任务创建时间。</summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>任务状态最后更新时间。</summary>
        public DateTime UpdatedTime { get; set; }

        /// <summary>数据与文件都上传成功的时间；未全部完成时为空。</summary>
        public DateTime? CompletedTime { get; set; }
    }
}
