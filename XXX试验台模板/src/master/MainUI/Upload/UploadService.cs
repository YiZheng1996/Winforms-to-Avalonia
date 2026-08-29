using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace MainUI.Upload
{
    /// <summary>
    /// 试验数据上传统一服务。
    /// 负责整次自动试验的数据累加、两个 HTTP 接口调用以及本地上传状态维护。
    /// 当前功能规模较小，相关逻辑集中在一个服务中，避免不必要的分层和类文件。
    /// </summary>
    internal sealed class UploadService : IDisposable
    {
        /// <summary>文件接口要求的固定业务类型。</summary>
        public const string BusinessType = "bedstand";

        private static UploadService _instance;
        private readonly UploadConfig _config;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _uploadLock = new(1, 1);
        private readonly object _sessionLock = new();

        /// <summary>当前整次自动试验正在累加的接口请求。</summary>
        private ProcessRecordRequest _currentRequest;

        /// <summary>AddProcessRecord 是否已在试验完成阶段上传成功。</summary>
        private bool _currentDataUploaded;

        /// <summary>
        /// 当前整场自动试验是否已经正常执行完全部已选小项点。
        /// 只有该状态为 true，才允许提交 AddProcessRecord 或保存其人工补传快照。
        /// </summary>
        private bool _currentTestCompleted;

        /// <summary>AddProcessRecord 最近一次失败信息。</summary>
        private string _currentDataUploadError = string.Empty;

        private UploadService(UploadConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, config.TimeoutSeconds))
            };
        }

        /// <summary>当前进程唯一的上传服务实例。</summary>
        public static UploadService Instance =>
            _instance ?? throw new InvalidOperationException("上传服务尚未初始化");

        /// <summary>当前上传配置，供试验完成界面判断 Auto 或 Prompt。</summary>
        public UploadConfig Config => _config;

        /// <summary>
        /// 程序启动时初始化服务。这里只创建配置和 HttpClient，不启动后台线程。
        /// </summary>
        public static void Initialize()
        {
            _instance ??= new UploadService(new UploadConfig());
        }

        /// <summary>程序退出时释放 HTTP 资源。</summary>
        public static void Stop()
        {
            _instance?.Dispose();
            _instance = null;
        }

        /// <summary>
        /// 开始一整套新的自动试验。
        /// testData 只在这里清空一次；后续每个小项点执行完成后都向同一个列表追加数据。
        /// </summary>
        public void BeginTest(string productSeqNo, string remark)
        {
            lock (_sessionLock)
            {
                _currentRequest = new ProcessRecordRequest
                {
                    DeviceName = _config.DeviceName ?? string.Empty,
                    DeviceCode = _config.DeviceCode ?? string.Empty,
                    ProductSeqNo = productSeqNo ?? string.Empty,
                    Remark = remark ?? string.Empty,
                    TestData = []
                };

                _currentDataUploaded = false;
                _currentTestCompleted = false;
                _currentDataUploadError = string.Empty;
            }
        }

        /// <summary>
        /// 在一个小项点的 Execute 完成后，将该项点结果追加到整套试验的 testData。
        /// 本方法只调用 List.Add，不会清空或覆盖之前已经完成的小项点。
        /// </summary>
        public void AddTestData(string testItem, string value, string unit, bool isOk)
        {
            lock (_sessionLock)
            {
                if (_currentRequest == null)
                    throw new InvalidOperationException("尚未开始自动试验上传会话");

                _currentRequest.TestData.Add(new TestDataItem
                {
                    TestItem = testItem ?? string.Empty,
                    Value = value ?? string.Empty,
                    Unit = unit ?? string.Empty,
                    IsOk = isOk
                });
            }
        }

        /// <summary>
        /// 标记整场自动试验已经执行完全部已选小项点。
        /// 此方法只改变完成状态，不上传数据；随后再根据 Auto/Prompt 配置决定是否调用接口。
        /// </summary>
        public void CompleteTest()
        {
            lock (_sessionLock)
            {
                if (_currentRequest == null)
                    throw new InvalidOperationException("尚未开始自动试验上传会话");

                _currentTestCompleted = true;
            }
        }

        /// <summary>
        /// 获取当前整套试验的 JSON 快照。
        /// 调用时 testData 已包含所有执行完成的小项点，顺序与自动试验执行顺序一致。
        /// </summary>
        public string GetCurrentPayloadJson()
        {
            lock (_sessionLock)
            {
                if (_currentRequest == null)
                    throw new InvalidOperationException("当前没有可上传的自动试验数据");

                if (!_currentTestCompleted)
                    throw new InvalidOperationException("当前自动试验尚未完整结束，不能上传试验数据");

                return JsonConvert.SerializeObject(_currentRequest);
            }
        }

        /// <summary>
        /// 所有小项点正常完成后调用 AddProcessRecord 一次。
        /// 失败后只保留错误，不执行后台重试。
        /// </summary>
        public async Task UploadCurrentTestDataOnceAsync(CancellationToken cancellationToken = default)
        {
            string payloadJson = GetCurrentPayloadJson();
            try
            {
                await UploadProcessRecordAsync(payloadJson, cancellationToken);
                _currentDataUploaded = true;
                _currentDataUploadError = string.Empty;
            }
            catch (Exception ex)
            {
                _currentDataUploaded = false;
                _currentDataUploadError = $"试验数据上传失败：{ex.Message}";
                throw;
            }
        }

        /// <summary>
        /// 保存报表后创建本地上传状态记录。
        /// 完整自动试验存在时保存 JSON 快照，供人工补传；没有完整自动试验时保存空快照，
        /// 仅处理 XLS 文件上传。由此保证保存报表功能不依赖 AddProcessRecord 会话。
        /// </summary>
        public long CreateUploadRecord(TestRecordModel record, string reportPath)
        {
            DateTime now = DateTime.Now;
            string payloadJson;
            bool dataUploaded;
            string dataUploadError;

            lock (_sessionLock)
            {
                bool hasCompletedTestData = _currentRequest != null && _currentTestCompleted;
                payloadJson = hasCompletedTestData
                    ? JsonConvert.SerializeObject(_currentRequest)
                    : string.Empty;

                // 没有完整自动试验数据时，表示本任务无需上传数据，人工重传只处理 XLS。
                dataUploaded = !hasCompletedTestData || _currentDataUploaded;
                dataUploadError = hasCompletedTestData ? _currentDataUploadError : string.Empty;
            }

            var model = new UploadTaskModel
            {
                RecordID = record.ID,
                PayloadJson = payloadJson,
                ReportPath = reportPath,
                DataUploaded = dataUploaded,
                FileUploaded = false,
                LastError = dataUploadError,
                CreatedTime = now,
                UpdatedTime = now
            };

            return VarHelper.fsql.Insert(model).ExecuteIdentity();
        }

        /// <summary>
        /// 保存报表按钮调用：只上传一次 XLS，并立即保存成功或失败状态。
        /// </summary>
        public async Task<UploadTaskModel> UploadFileOnceAsync(
            long uploadTaskID, CancellationToken cancellationToken = default)
        {
            await _uploadLock.WaitAsync(cancellationToken);
            try
            {
                UploadTaskModel task = GetUploadTask(uploadTaskID)
                    ?? throw new InvalidOperationException("未找到本地上传状态记录");

                if (task.FileUploaded)
                    return task;

                try
                {
                    Guid? fileID = await UploadFileAsync(task.ReportPath, cancellationToken);
                    task.FileUploaded = true;
                    task.ServerFileID = fileID?.ToString();
                    if (task.DataUploaded)
                    {
                        task.LastError = string.Empty;
                        task.CompletedTime = DateTime.Now;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    task.LastError = $"文件上传失败：{ex.Message}";
                }

                task.UpdatedTime = DateTime.Now;
                UpdateUploadTask(task);
                return task;
            }
            finally
            {
                _uploadLock.Release();
            }
        }

        /// <summary>
        /// 数据管理页面人工重传。已成功的部分跳过，未成功的部分各尝试一次。
        /// </summary>
        public async Task<UploadTaskModel> RetryOnceAsync(
            int recordID, CancellationToken cancellationToken = default)
        {
            await _uploadLock.WaitAsync(cancellationToken);
            try
            {
                UploadTaskModel task = GetUploadTaskByRecordID(recordID)
                    ?? throw new InvalidOperationException("当前记录没有可重新上传的数据");

                if (task.DataUploaded && task.FileUploaded)
                    return task;

                string latestError = string.Empty;

                if (!task.DataUploaded)
                {
                    try
                    {
                        await UploadProcessRecordAsync(task.PayloadJson, cancellationToken);
                        task.DataUploaded = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        latestError = $"试验数据上传失败：{ex.Message}";
                    }
                }

                if (!task.FileUploaded)
                {
                    try
                    {
                        Guid? fileID = await UploadFileAsync(task.ReportPath, cancellationToken);
                        task.FileUploaded = true;
                        task.ServerFileID = fileID?.ToString();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        string fileError = $"文件上传失败：{ex.Message}";
                        latestError = string.IsNullOrWhiteSpace(latestError)
                            ? fileError
                            : $"{latestError}；{fileError}";
                    }
                }

                task.LastError = latestError;
                task.UpdatedTime = DateTime.Now;
                if (task.DataUploaded && task.FileUploaded)
                    task.CompletedTime = DateTime.Now;

                UpdateUploadTask(task);
                return task;
            }
            finally
            {
                _uploadLock.Release();
            }
        }

        /// <summary>批量读取报表管理列表需要的上传状态。</summary>
        public List<UploadTaskModel> GetUploadTasksByRecordIDs(IEnumerable<int> recordIDs)
        {
            int[] ids = recordIDs.Distinct().ToArray();
            if (ids.Length == 0)
                return [];

            return VarHelper.fsql.Select<UploadTaskModel>()
                .Where(task => ids.Contains(task.RecordID))
                .OrderByDescending(task => task.ID)
                .ToList();
        }

        /// <summary>删除试验记录时同步删除其上传状态。</summary>
        public void DeleteByRecordID(int recordID)
        {
            VarHelper.fsql.Delete<UploadTaskModel>()
                .Where(task => task.RecordID == recordID)
                .ExecuteAffrows();
        }

        private UploadTaskModel GetUploadTask(long id) => VarHelper.fsql
            .Select<UploadTaskModel>()
            .Where(task => task.ID == id)
            .First();

        private UploadTaskModel GetUploadTaskByRecordID(int recordID) => VarHelper.fsql
            .Select<UploadTaskModel>()
            .Where(task => task.RecordID == recordID)
            .OrderByDescending(task => task.ID)
            .First();

        private static void UpdateUploadTask(UploadTaskModel model)
        {
            VarHelper.fsql.Update<UploadTaskModel>()
                .SetSource(model)
                .ExecuteAffrows();
        }

        /// <summary>调用 /api/report/AddProcessRecord。</summary>
        private async Task UploadProcessRecordAsync(
            string payloadJson, CancellationToken cancellationToken)
        {
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(
                BuildUri("api/report/AddProcessRecord"), content, cancellationToken);
            await EnsureSuccessAsync<object>(response, cancellationToken);
        }

        /// <summary>
        /// 调用 /api/file/upload。businessType 固定为 bedstand，businessId 固定为空 Guid。
        /// </summary>
        private async Task<Guid?> UploadFileAsync(
            string filePath, CancellationToken cancellationToken)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("待上传的试验报表不存在", filePath);

            await using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ms-excel");

            form.Add(fileContent, "file", Path.GetFileName(filePath));
            form.Add(new StringContent(BusinessType), "businessType");
            form.Add(new StringContent(Guid.Empty.ToString()), "businessId");

            using var response = await _httpClient.PostAsync(
                BuildUri("api/file/upload"), form, cancellationToken);
            ApiResponse<FileUploadResult> result =
                await EnsureSuccessAsync<FileUploadResult>(response, cancellationToken);
            return result.Data?.ID;
        }

        private Uri BuildUri(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(_config.BaseUrl))
                throw new InvalidOperationException("未配置数据上传服务器地址");

            string baseUrl = _config.BaseUrl.Trim();
            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri baseUri))
                throw new InvalidOperationException($"数据上传服务器地址无效：{_config.BaseUrl}");

            return new Uri(baseUri, relativePath);
        }

        /// <summary>统一校验 HTTP 状态码和业务 code（0 表示成功）。</summary>
        private static async Task<ApiResponse<T>> EnsureSuccessAsync<T>(
            HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"接口返回 HTTP {(int)response.StatusCode}：{responseText}");
            }

            ApiResponse<T> result;
            try
            {
                result = JsonConvert.DeserializeObject<ApiResponse<T>>(responseText);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"接口响应不是有效 JSON：{responseText}", ex);
            }

            if (result == null)
                throw new InvalidOperationException("接口响应为空");

            if (result.Code != 0)
            {
                string message = !string.IsNullOrWhiteSpace(result.Message)
                    ? result.Message
                    : result.Error;
                throw new InvalidOperationException($"接口返回失败，code={result.Code}：{message}");
            }

            return result;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _uploadLock.Dispose();
        }
    }
}
