namespace XXX.TestBench.App.Localization;

/// <summary>
/// 审计日志的界面显示文本转换器。
/// 审计库中的动作码保持稳定的内部值，只有显示层转换为中文，避免影响历史数据和查询语义。
/// </summary>
public static class LogTextLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> ActionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["LoginFailed"] = "登录失败",
        ["LoginBlocked"] = "登录被拒绝",
        ["LoginLocked"] = "登录被锁定",
        ["LoginSucceeded"] = "登录成功",
        ["PasswordChanged"] = "修改密码",
        ["SessionRevoked"] = "退出登录",
        ["AccessDenied"] = "权限不足",
        ["DeviceModeInitialized"] = "设备模式初始化",
        ["DeviceModeInitFailed"] = "设备模式初始化失败",
        ["DevicePointCatalogSaved"] = "保存设备点位",
        ["DeviceWrite"] = "设备点位写入",
        ["ProductTypeCreated"] = "新增产品类型",
        ["ProductTypeEnabled"] = "启用产品类型",
        ["ProductTypeDisabled"] = "停用产品类型",
        ["ProductTypeRenamed"] = "重命名产品类型",
        ["ProductTypeDeleted"] = "删除产品类型",
        ["ProductModelCreated"] = "新增产品型号",
        ["ProductModelEnabled"] = "启用产品型号",
        ["ProductModelDisabled"] = "停用产品型号",
        ["ProductModelRenamed"] = "重命名产品型号",
        ["ProductModelDeleted"] = "删除产品型号",
        ["TestPointCreated"] = "新增试验项点",
        ["TestPointUpdated"] = "修改试验项点",
        ["TestPointDeleted"] = "删除试验项点",
        ["ModelPointConfigSaved"] = "保存型号试验项点配置",
        ["ProjectTestParameterSaved"] = "保存项目级试验参数",
        ["ProductTestParameterSaved"] = "保存产品试验参数",
        // 兼容升级前已经写入的历史动作码；当前页面不再产生这两类记录。
        ["TypeTestParameterSaved"] = "保存产品类型试验参数",
        ["ModelTestParameterSaved"] = "保存产品型号试验参数",
        ["TestStarted"] = "开始试验",
        ["ItemExecuted"] = "执行试验项点",
        ["TestFinished"] = "结束试验",
        ["ReportGenerated"] = "生成报表成功",
        ["ReportFailed"] = "生成报表失败",

        // 兼容早期任务/配方版本可能已经写入数据库的动作码。
        ["TaskCreated"] = "新增试验任务",
        ["TaskUpdated"] = "修改试验任务",
        ["TaskCancelled"] = "取消试验任务",
        ["TaskStarted"] = "开始试验任务",
        ["TaskCompleted"] = "完成试验任务",
        ["RecipeCreated"] = "新增配方",
        ["RecipeUpdated"] = "修改配方",
        ["RecipePublished"] = "发布配方",
        ["RecipeDisabled"] = "停用配方",
        ["RecipeDeleted"] = "删除配方",
        ["UserCreated"] = "新增用户",
        ["UserUpdated"] = "修改用户",
        ["UserEnabled"] = "启用用户",
        ["UserDisabled"] = "停用用户"
    };

    /// <summary>
    /// 已知动作码及其显示名称，供日志查询下拉框使用。
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> KnownActions { get; } =
        ActionNames.OrderBy(pair => pair.Value, StringComparer.CurrentCulture).ToArray();

    private static readonly IReadOnlyDictionary<string, string> PermissionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ViewOverview"] = "查看运行总览",
        ["ManageTestPoints"] = "管理试验项点",
        ["ManageTasks"] = "管理试验任务",
        ["ExecuteTests"] = "执行试验",
        ["ManualControl"] = "手动控制",
        ["ManageProducts"] = "管理产品类型与型号",
        ["ManageTestDefinitions"] = "管理试验参数",
        ["ManageRecipes"] = "管理配方",
        ["ViewRecords"] = "查看试验记录",
        ["GenerateReports"] = "生成报表",
        ["ManageDevices"] = "管理设备",
        ["CalibrateDevices"] = "设备校准",
        ["ManageUsers"] = "管理用户",
        ["ViewLogs"] = "查看日志"
    };

    /// <summary>
    /// 转换操作者显示文本。普通登录账号保留原值，系统保留字转换为客户可读名称。
    /// </summary>
    public static string Actor(string? actor)
    {
        var value = actor?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "未知用户";
        return value.ToLowerInvariant() switch
        {
            "unknown" => "未知用户",
            "system" => "系统",
            "session" => "系统会话",
            _ => value
        };
    }

    /// <summary>
    /// 转换动作显示文本。未知内部动作不直接暴露英文码，避免客户看到无法理解的技术标识。
    /// </summary>
    public static string Action(string? action)
    {
        var value = action?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "未知操作";
        return ActionNames.TryGetValue(value, out var display)
            ? display
            : ContainsChinese(value) ? value : "其他系统操作";
    }

    /// <summary>
    /// 转换操作目标显示文本。
    /// </summary>
    public static string Target(string? action, string? target)
    {
        var value = target?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "未指定";

        if (TryReadProductTarget(value, out var productTypeId, out var productModelId))
            return $"产品参数（类型编号：{productTypeId}；型号编号：{productModelId}）";
        if (TryReadPrefixedValue(value, "type:", out var typeId, out _))
            return $"产品类型（编号：{typeId}）";
        if (TryReadPrefixedValue(value, "model:", out var modelId, out _))
            return $"产品型号（编号：{modelId}）";
        if (TryReadPrefixedValue(value, "record:", out var recordId, out _))
            return $"试验记录（编号：{recordId}）";
        if (TryReadPrefixedValue(value, "point:", out var pointId, out _))
            return $"试验项点（编号：{pointId}）";
        if (PermissionNames.TryGetValue(value, out var permission))
            return $"权限：{permission}";
        if (value.Equals("project", StringComparison.OrdinalIgnoreCase)) return "项目级参数";
        if (value.Equals("points.json", StringComparison.OrdinalIgnoreCase)) return "设备点位配置文件";
        if (value.Equals("Simulation", StringComparison.OrdinalIgnoreCase)) return "仿真模式";
        if (value.Equals("Hardware", StringComparison.OrdinalIgnoreCase)) return "硬件模式";

        return action?.Trim() switch
        {
            "DeviceWrite" => $"设备点位：{value}",
            "SessionRevoked" => $"会话编号：{value}",
            "LoginFailed" or "LoginBlocked" or "LoginLocked" or "LoginSucceeded" or "PasswordChanged"
                => $"登录账号：{value}",
            _ => value
        };
    }

    /// <summary>
    /// 转换操作详情显示文本，并把布尔值、试验结果、参数单位等内部格式转换为中文。
    /// </summary>
    public static string Detail(string? action, string? detail)
    {
        var value = detail?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "无";

        if (action?.Equals("DeviceWrite", StringComparison.OrdinalIgnoreCase) == true
            && TryReadWriteValues(value, out var written, out var readback))
            return $"写入值：{FormatValue(written)}；回读值：{FormatValue(readback)}";

        if (action?.Equals("DevicePointCatalogSaved", StringComparison.OrdinalIgnoreCase) == true
            && TryReadPrefixedValue(value, "points:", out var pointCount, out _))
            return $"点位数量：{pointCount}";

        if (action?.Equals("ProjectTestParameterSaved", StringComparison.OrdinalIgnoreCase) == true)
            return $"试验时长：{FormatValue(value)} 秒";
        if (action?.Equals("ProductTestParameterSaved", StringComparison.OrdinalIgnoreCase) == true
            && TryReadProductParameterValues(value, out var voltage, out var current))
            return $"试验电压：{FormatValue(voltage)} 伏；保护电流：{FormatValue(current)} 毫安";
        if (action?.Equals("TypeTestParameterSaved", StringComparison.OrdinalIgnoreCase) == true)
            return $"试验电压：{FormatValue(value)} 伏";
        if (action?.Equals("ModelTestParameterSaved", StringComparison.OrdinalIgnoreCase) == true)
            return $"保护电流：{FormatValue(value)} 毫安";

        if (action?.Equals("ModelPointConfigSaved", StringComparison.OrdinalIgnoreCase) == true)
            return $"试验项点顺序：{FormatPointSequence(value)}";

        if (action?.Equals("AccessDenied", StringComparison.OrdinalIgnoreCase) == true)
            return $"涉及对象：{FormatValue(value)}";

        if (action?.Equals("TestPointCreated", StringComparison.OrdinalIgnoreCase) == true
            && TryReadPrefixedValue(value, "point:", out var createdPointId, out var createdPointName))
            return string.IsNullOrWhiteSpace(createdPointName)
                ? $"试验项点编号：{createdPointId}"
                : $"试验项点编号：{createdPointId}；名称：{createdPointName}";

        if (action?.Equals("TestStarted", StringComparison.OrdinalIgnoreCase) == true
            && TryReadPrefixedValue(value, "model:", out var startedModelId, out var recordNumber))
            return string.IsNullOrWhiteSpace(recordNumber)
                ? $"产品型号编号：{startedModelId}"
                : $"产品型号编号：{startedModelId}；记录编号：{recordNumber}";

        if (action?.Equals("ItemExecuted", StringComparison.OrdinalIgnoreCase) == true
            && TryReadPrefixedValue(value, "point:", out var executedPointId, out var result))
            return string.IsNullOrWhiteSpace(result)
                ? $"试验项点编号：{executedPointId}"
                : $"试验项点编号：{executedPointId}；结果：{FormatValue(result)}";

        if (action?.EndsWith("Renamed", StringComparison.OrdinalIgnoreCase) == true
            && TryReadRename(value, out var oldName, out var newName))
            return $"原名称：{oldName}；新名称：{newName}";

        if (action?.Equals("ReportGenerated", StringComparison.OrdinalIgnoreCase) == true)
            return $"输出文件：{value}";
        if (action?.Equals("ReportFailed", StringComparison.OrdinalIgnoreCase) == true)
            return $"失败原因：{value}";
        if (action?.Equals("DeviceModeInitFailed", StringComparison.OrdinalIgnoreCase) == true)
            return $"失败原因：{value}";

        return FormatValue(value);
    }

    private static string FormatValue(string value)
        => value.Trim() switch
        {
            "true" or "True" or "TRUE" => "是",
            "false" or "False" or "FALSE" => "否",
            "Passed" or "passed" => "合格",
            "Failed" or "failed" => "不合格",
            "Pending" or "pending" => "待执行",
            "Running" or "running" => "执行中",
            "Completed" or "completed" => "已完成",
            "Skipped" or "skipped" => "已跳过",
            "Aborted" or "aborted" => "已中止",
            "Simulation" or "simulation" => "仿真",
            "Hardware" or "hardware" => "硬件",
            "Healthy" or "healthy" => "正常",
            "Degraded" or "degraded" => "降级",
            "Faulted" or "faulted" => "故障",
            "Disconnected" or "disconnected" => "断开",
            "Unknown" or "unknown" => "未知",
            "Good" or "good" => "良好",
            "Stale" or "stale" => "陈旧",
            "Bad" or "bad" => "无效",
            "Normal" or "normal" => "普通",
            "HighRisk" or "highrisk" => "高风险",
            _ => value.Trim()
        };

    private static string FormatPointSequence(string value)
        => string.Join("、", value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool TryReadWriteValues(string value, out string written, out string readback)
    {
        const string valuePrefix = "value=";
        const string readbackPrefix = "readback=";
        written = string.Empty;
        readback = string.Empty;
        if (!value.StartsWith(valuePrefix, StringComparison.OrdinalIgnoreCase)) return false;

        var separator = value.IndexOf(readbackPrefix, valuePrefix.Length, StringComparison.OrdinalIgnoreCase);
        if (separator < 0) return false;
        written = value[valuePrefix.Length..separator].Trim().TrimEnd(';', ',');
        readback = value[(separator + readbackPrefix.Length)..].Trim();
        return written.Length > 0 && readback.Length > 0;
    }

    private static bool TryReadRename(string value, out string oldName, out string newName)
    {
        oldName = string.Empty;
        newName = string.Empty;
        var separator = value.IndexOf("->", StringComparison.Ordinal);
        if (separator < 0) return false;
        oldName = value[..separator].Trim();
        newName = value[(separator + 2)..].Trim();
        return oldName.Length > 0 && newName.Length > 0;
    }

    private static bool TryReadPrefixedValue(string value, string prefix, out string identifier, out string remainder)
    {
        identifier = string.Empty;
        remainder = string.Empty;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var payload = value[prefix.Length..].Trim();
        if (payload.Length == 0) return false;
        var separator = payload.IndexOf(' ');
        if (separator < 0)
        {
            identifier = payload;
            return true;
        }

        identifier = payload[..separator].Trim();
        remainder = payload[(separator + 1)..].Trim();
        return identifier.Length > 0;
    }

    private static bool TryReadProductTarget(string value, out string typeId, out string modelId)
    {
        typeId = string.Empty;
        modelId = string.Empty;
        if (!value.StartsWith("type:", StringComparison.OrdinalIgnoreCase)) return false;

        var separator = value.IndexOf("/model:", StringComparison.OrdinalIgnoreCase);
        if (separator <= "type:".Length) return false;
        typeId = value["type:".Length..separator].Trim();
        modelId = value[(separator + "/model:".Length)..].Trim();
        return typeId.Length > 0 && modelId.Length > 0;
    }

    private static bool TryReadProductParameterValues(string value, out string voltage, out string current)
    {
        voltage = string.Empty;
        current = string.Empty;
        if (!value.StartsWith("voltage:", StringComparison.OrdinalIgnoreCase)) return false;

        var separator = value.IndexOf(";current:", StringComparison.OrdinalIgnoreCase);
        if (separator <= "voltage:".Length) return false;
        voltage = value["voltage:".Length..separator].Trim();
        current = value[(separator + ";current:".Length)..].Trim();
        return voltage.Length > 0 && current.Length > 0;
    }

    private static bool ContainsChinese(string value)
        => value.Any(character => character is >= '\u4e00' and <= '\u9fff');
}
