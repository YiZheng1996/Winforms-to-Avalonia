using CommunityToolkit.Mvvm.ComponentModel;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Execution;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>编辑单个产品型号的 Excel 判定和过程报告资源。</summary>
public sealed partial class ModelResourceDialogViewModel : ObservableObject
{
    private readonly ModelResourceService _service;
    private readonly ProductModel _model;
    private readonly UserContext _actor;

    public ModelResourceDialogViewModel(ModelResourceService service, ProductModel model, UserContext actor)
    {
        _service = service;
        _model = model;
        _actor = actor;
    }

    public string ModelName => _model.Name;
    public string ExistingProfileText { get; private set; } = "正在读取当前资源配置…";
    public bool IsBusy { get; private set; }
    public bool HasGuidance { get; private set; }

    [ObservableProperty] private string _evaluationTemplatePath = string.Empty;
    [ObservableProperty] private string _evaluationMappingPath = string.Empty;
    [ObservableProperty] private string _processTemplatePath = string.Empty;
    [ObservableProperty] private string _processMappingPath = string.Empty;
    [ObservableProperty] private string _guidancePackagePath = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>加载已保存配置摘要；不将旧路径作为资源文件的来源。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _service.GetProfileAsync(_model.Id, cancellationToken);
        HasGuidance = profile?.GuidanceKey is not null;
        OnPropertyChanged(nameof(HasGuidance));
        ExistingProfileText = profile is null
            ? "尚未配置报表资源。"
            : $"配置版本 {profile.Revision}；产品判定已配置，过程报告{(profile.ProcessTemplateKey is null ? "未配置" : "已配置")}，准备指导{(profile.GuidanceKey is null ? "未配置" : "已配置。每个资源均按摘要锁定")}。";
        OnPropertyChanged(nameof(ExistingProfileText));
    }

    /// <summary>读取已导入指导包供管理员预览，不确认准备状态或放行试验。</summary>
    public Task<ModelPreparationGuidanceSnapshot?> LoadGuidancePreviewAsync(CancellationToken cancellationToken = default)
        => _service.GetPreparationGuidanceAsync(_model.Id, cancellationToken);

    /// <summary>导入已选择的模板和映射；首次配置先保存产品判定资源。</summary>
    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return false;
        if (!HasPair(EvaluationTemplatePath, EvaluationMappingPath)
            || !HasPair(ProcessTemplatePath, ProcessMappingPath))
        {
            StatusMessage = "每类报告的 Excel 模板和 JSON 映射必须成对选择。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(EvaluationTemplatePath)
            && string.IsNullOrWhiteSpace(ProcessTemplatePath)
            && string.IsNullOrWhiteSpace(GuidancePackagePath))
        {
            StatusMessage = "请选择需要更新的模板/映射或准备指导 ZIP 包。";
            return false;
        }

        IsBusy = true;
        OnPropertyChanged(nameof(IsBusy));
        try
        {
            var imported = new List<string>();
            if (!string.IsNullOrWhiteSpace(EvaluationTemplatePath))
            {
                var profile = await _service.ImportReportPairAsync(_model.Id, ReportPurpose.ProductEvaluation,
                    EvaluationTemplatePath, EvaluationMappingPath, _actor, cancellationToken);
                imported.Add($"产品判定版本 {profile.Revision}");
                EvaluationTemplatePath = EvaluationMappingPath = string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(ProcessTemplatePath))
            {
                var profile = await _service.ImportReportPairAsync(_model.Id, ReportPurpose.ProcessOnly,
                    ProcessTemplatePath, ProcessMappingPath, _actor, cancellationToken);
                imported.Add($"过程报告版本 {profile.Revision}");
                ProcessTemplatePath = ProcessMappingPath = string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(GuidancePackagePath))
            {
                var profile = await _service.ImportGuidancePackageAsync(_model.Id, GuidancePackagePath, _actor, cancellationToken);
                imported.Add($"准备指导版本 {profile.Revision}");
                GuidancePackagePath = string.Empty;
            }

            StatusMessage = string.Join("；", imported) + "已导入并保存。";
            await LoadAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private static bool HasPair(string templatePath, string mappingPath)
        => string.IsNullOrWhiteSpace(templatePath) == string.IsNullOrWhiteSpace(mappingPath);
}
