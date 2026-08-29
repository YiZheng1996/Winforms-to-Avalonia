using RW.UI.Controls.Report;
using MainUI.Config;

namespace MainUI.Service
{
    /// <summary>
    /// 报表服务类
    /// 提供报表相关的业务逻辑处理,包括报表控件管理、文件操作、翻页等功能
    /// </summary>
    public class ReportService
    {
        #region 私有字段

        private readonly string _reportPath;
        private readonly RWReport _report;
        private int _currentRows = 1;
        private const int MaxRows = 1000;

        // ===== 新增: 报表控件单例管理 =====
        private static RWReport _sharedReportControl;
        private static readonly object _lock = new object();

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="reportPath">报表路径</param>
        /// <param name="report">报表控件</param>
        public ReportService(string reportPath, RWReport report)
        {
            _reportPath = reportPath ?? throw new ArgumentNullException(nameof(reportPath));
            _report = report ?? throw new ArgumentNullException(nameof(report));

            // 注册报表控件到共享实例
            RegisterReportControl(report);
        }

        #endregion

        #region ===== 新增功能: 报表控件单例管理 =====

        /// <summary>
        /// 注册报表控件到共享实例
        /// </summary>
        /// <param name="report">报表控件实例</param>
        public static void RegisterReportControl(RWReport report)
        {
            lock (_lock)
            {
                if (_sharedReportControl != report)
                {
                    _sharedReportControl = report;

                    // 同时更新 BaseTest.Report (保持向后兼容)
                    BaseTest.Report = report;

                    NlogHelper.Default.Info($"报表控件已注册到全局服务 (实例: {report?.GetHashCode()})");
                }
            }
        }

        /// <summary>
        /// 获取共享的报表控件实例
        /// </summary>
        /// <returns>报表控件实例</returns>
        /// <exception cref="InvalidOperationException">报表控件未初始化时抛出</exception>
        public static RWReport GetReportControl()
        {
            lock (_lock)
            {
                if (_sharedReportControl == null || _sharedReportControl.IsDisposed)
                {
                    throw new InvalidOperationException(
                        "报表控件未初始化,请确保:\n" +
                        "1. 已在HMI界面加载报表\n" +
                        "2. UcHMI.Init() 已正确执行\n" +
                        "3. 报表文件路径正确");
                }

                return _sharedReportControl;
            }
        }

        /// <summary>
        /// 检查报表控件是否可用
        /// </summary>
        public static bool IsReportAvailable
        {
            get
            {
                lock (_lock)
                {
                    return _sharedReportControl != null && !_sharedReportControl.IsDisposed;
                }
            }
        }

        /// <summary>
        /// 在UI线程安全执行报表操作(有返回值)
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="action">要执行的操作</param>
        /// <returns>操作结果</returns>
        public static T InvokeOnReportControl<T>(Func<RWReport, T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var report = GetReportControl();

            if (report.InvokeRequired)
            {
                return (T)report.Invoke(action, report);
            }
            else
            {
                return action(report);
            }
        }

        /// <summary>
        /// 在UI线程安全执行报表操作(无返回值)
        /// </summary>
        /// <param name="action">要执行的操作</param>
        public static void InvokeOnReportControl(Action<RWReport> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var report = GetReportControl();

            if (report.InvokeRequired)
            {
                report.Invoke(action, report);
            }
            else
            {
                action(report);
            }
        }

        /// <summary>
        /// 清除共享的报表控件引用
        /// (通常在应用程序关闭或重置时调用)
        /// </summary>
        public static void ClearReportControl()
        {
            lock (_lock)
            {
                _sharedReportControl = null;
                BaseTest.Report = null;
                NlogHelper.Default.Info("报表控件引用已清除");
            }
        }

        #endregion

        #region ===== 原有功能保持不变 =====

        /// <summary>
        /// 获取默认报表路径
        /// </summary>
        public static string GetDefaultReportPath()
        {
            return Path.Combine(Application.StartupPath, "reports\\");
        }

        /// <summary>
        /// 获取工作报表路径
        /// </summary>
        public static string GetWorkingReportPath()
        {
            return Path.Combine(GetDefaultReportPath(), "report.xls");
        }

        /// <summary>
        /// 解析并确保报表保存目录可用。
        /// 配置目录不可用时回退到当前用户文档目录或本地应用数据目录，
        /// 避免将试验结果写入程序的 bin\Debug 目录。
        /// </summary>
        public static string ResolveReportDirectory(string configuredPath)
        {
            string preferredPath = string.IsNullOrWhiteSpace(configuredPath)
                ? SaveReportConfig.DefaultReportDirectory
                : configuredPath.Trim();

            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] candidates =
            [
                preferredPath,
                string.IsNullOrWhiteSpace(documentsPath)
                    ? null
                    : Path.Combine(documentsPath, "试验报表"),
                string.IsNullOrWhiteSpace(localAppDataPath)
                    ? null
                    : Path.Combine(localAppDataPath, "试验报表")
            ];

            foreach (string candidate in candidates)
            {
                if (TryEnsureDirectory(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException("报表保存目录不可用，请检查磁盘和目录权限。");
        }

        private static bool TryEnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(path);
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取参数管理界面配置的试验报表保存根目录。
        /// </summary>
        public static string GetConfiguredSaveReportPath()
        {
            try
            {
                SaveReportConfig config = new();
                config.Load();

                string configuredPath = string.IsNullOrWhiteSpace(config.RptSaveFile)
                    ? SaveReportConfig.DefaultReportDirectory
                    : config.RptSaveFile.Trim();
                string resolvedPath = ResolveReportDirectory(configuredPath);

                if (!string.Equals(configuredPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
                {
                    NlogHelper.Default.Warn(
                        $"报表配置目录不可用，已回退到可用目录：{configuredPath} -> {resolvedPath}");
                }

                return resolvedPath;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Warn($"读取报表保存路径失败，尝试使用默认目录：{ex.Message}");
            }

            return ResolveReportDirectory(SaveReportConfig.DefaultReportDirectory);
        }

        /// <summary>
        /// 构建保存文件路径
        /// </summary>
        public static string BuildSaveFilePath(string modelName)
        {
            string savePath = Path.Combine(GetConfiguredSaveReportPath(),
                DateTime.Now.ToString("yyyy"),
                DateTime.Now.ToString("MM"));

            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            string fileName = $"{modelName}_{DateTime.Now:yyyyMMddHHmmss}.xls";
            return Path.Combine(savePath, fileName);
        }

        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        public bool FileExists(string fileName)
        {
            return File.Exists(fileName);
        }

        /// <summary>
        /// 复制报表文件
        /// </summary>
        public void CopyReportFile(string sourceFile, string destFile)
        {
            try
            {
                if (File.Exists(destFile))
                {
                    File.Delete(destFile);
                }

                File.Copy(sourceFile, destFile, true);
                NlogHelper.Default.Info($"报表文件已复制: {sourceFile} -> {destFile}");
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"复制报表文件失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 保存试验记录并把 SQLite 自增 ID 回填到 record.ID。
        /// 上传状态表需要该 ID 与本地试验记录建立关系，供报表管理页面查询和人工重传。
        /// </summary>
        public static bool SaveTestRecord(TestRecordModel record)
        {
            try
            {
                if (record == null)
                    throw new ArgumentNullException(nameof(record));

                // ExecuteIdentity 与原 ExecuteAffrows 的区别是会返回新记录主键。
                long identity = VarHelper.fsql.Insert(record).ExecuteIdentity();

                if (identity > 0)
                {
                    // Record 模型主键为 int，checked 可在异常超出 int 范围时明确报错。
                    record.ID = checked((int)identity);
                    NlogHelper.Default.Info($"试验记录保存成功: {record.TestID}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"保存试验记录失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 向上翻页
        /// </summary>
        public (int currentRows, bool upEnabled, bool downEnabled) PageUp(int pageSize)
        {
            _currentRows -= pageSize;
            if (_currentRows < 1)
            {
                _currentRows = 1;
            }

            _report?.ScrollIndex(_currentRows);

            return (_currentRows, _currentRows > 1, _currentRows < MaxRows);
        }

        /// <summary>
        /// 向下翻页
        /// </summary>
        public (int currentRows, bool upEnabled, bool downEnabled) PageDown(int pageSize)
        {
            _currentRows += pageSize;
            if (_currentRows > MaxRows)
            {
                _currentRows = 1; // 循环到第一页
            }

            _report?.ScrollIndex(_currentRows);

            return (_currentRows, _currentRows > 1, _currentRows < MaxRows);
        }

        #endregion
    }

    static class Constants
    {
        public const string ReportsPath = @"reports\report.xls";
    }
}
