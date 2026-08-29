using FreeSql.DataAnnotations;

namespace MainUI.Model
{
    [Table(Name = "Record")]
    public class TestRecordModel
    {
        /// <summary>
        /// ID
        /// </summary>
        [Column(IsPrimary = true, IsIdentity = true)]
        public int ID { get; set; }

        /// <summary>
        /// 类型名称ID
        /// </summary>
        public int KindID { get; set; }

        /// <summary>
        /// 型号名称ID
        /// </summary>
        public int ModelID { get; set; }

        /// <summary>
        /// 车号
        /// </summary>
        [Column(StringLength = 200)]
        public string TestID { get; set; }

        /// <summary>
        /// 产品编号
        /// </summary>
        [Column(StringLength = 200)]
        public string ProductNumber { get; set; }

        /// <summary>
        /// 操作员
        /// </summary>
        [Column(StringLength = 100)]
        public string Tester { get; set; }

        /// <summary>
        /// 保存时间
        /// </summary>
        [Column(ServerTime = DateTimeKind.Local)]
        public DateTime TestTime { get; set; }

        /// <summary>
        /// 保存报告路径
        /// </summary>
        [Column(StringLength = 500)]
        public string ReportPath { get; set; }

    }

    public class TestRecordModelDto : TestRecordModel
    {
        /// <summary>
        /// 类型名称
        /// </summary>
        [Column(StringLength = 100)]
        public string ModelTypeName { get; set; }

        /// <summary>
        /// 型号名称
        /// </summary>
        [Column(StringLength = 100)]
        public string ModelName { get; set; }

        /// <summary>
        /// 数据管理页面显示的综合上传状态，由 UploadTask 表动态计算，不写入 Record 表。
        /// </summary>
        [Column(IsIgnore = true)]
        public string UploadStatus { get; set; }

        /// <summary>试验数据和 XLS 都成功上传的时间。</summary>
        [Column(IsIgnore = true)]
        public DateTime? UploadedTime { get; set; }

        /// <summary>最近一次数据或文件上传失败信息。</summary>
        [Column(IsIgnore = true)]
        public string UploadError { get; set; }

    }
}
