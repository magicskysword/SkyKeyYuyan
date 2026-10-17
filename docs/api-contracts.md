# SkyKeyYuyan 接口与数据契约

公共接口将语言处理、会话调度和文本目标分离。以下签名表达核心调用关系，数据表规定参数和结果必须携带的语义，供独立 .NET 实现及 Unity 适配共同遵循。

## 运行时入口

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 管理语言资源并创建独立输入会话。
/// </summary>
public interface ISkyRuntime : IDisposable
{
    ServiceResult RegisterLanguage(ILanguageModule module);

    InitializationResult Initialize(RuntimeOptions options);

    Task<InitializationResult> InitializeAsync(
        RuntimeOptions options,
        CancellationToken cancellationToken);

    InitializationResult InitializeLanguage(string engineId);

    Task<InitializationResult> InitializeLanguageAsync(
        string engineId,
        CancellationToken cancellationToken);

    SessionOpenResult OpenSession(
        SessionOptions options,
        ITextTarget target);
}
```

注册记录模块和工厂。首次初始化准备所配置模块，运行期间新增模块通过单模块初始化进入就绪状态。语言查询服务提供已注册模块及其就绪状态。

| 类型 | 必需内容与语义 |
| --- | --- |
| RuntimeOptions | 执行上下文、资源 Provider、词频存储 Provider、语言实现配置、是否取代全局默认实例（默认取代）、默认用户标识、预加载用户列表及服务选项 |
| ServiceResult | 是否成功、执行阶段、失败原因及取消状态；用于注册、模块准备和检查点恢复 |
| InitializationResult | 成功、失败或取消，初始化阶段，受影响模块，各模块就绪状态，用户词频预加载状态和诊断 |
| SessionOpenResult | 打开结果、成功时的会话、目标占用或语言未就绪等原因 |
| SessionOptions | 玩家标识、用户标识（可省略，省略时使用默认用户）、会话语言列表、初始语言、编辑模式和会话设置 |

初始化返回成功后，所要求的资源处于可用状态。异步取消走本次初始化的资源清理路径。用户词频预加载失败不影响初始化结果，记录在预加载状态中。会话使用就绪引擎；同一目标被占用时，打开操作返回可识别的结果。

### 运行时实例

一个进程中可以有多个 ISkyRuntime 实例，其中一个是全局默认实例。打开输入默认使用全局默认实例，也可以在指定实例上调用 OpenSession。

实例内的语言只能添加，不能注销；语言数据和实现配置不能在实例内替换。更换时初始化新实例，新实例初始化成功后默认取代全局默认实例，RuntimeOptions 可关闭这一行为；初始化失败的实例不会取代全局默认实例。

| 结束方式 | 语义 |
| --- | --- |
| 被替换后，最后一个会话结束 | 自动保存待写词频，释放实例资源，之后交给 GC 回收 |
| 显式调用 Dispose | 以结束原因「运行时释放」结束仍打开的会话，每个会话发布一次 Completed；随后保存待写词频，按依赖顺序释放资源 |

运行时只借用 RuntimeOptions 中的 Provider，从不调用其 Dispose；Provider 由宿主创建和释放。存档 Provider 在进程内只配置一次，见[词频与存档契约](#词频与存档契约)。

## 语言模块

语言模块通过 ILanguageModule 提供以下契约：

| 能力 | 内容 |
| --- | --- |
| 描述 | 模块、语言和引擎标识，显示名称，版本及能力描述 |
| 资源准备 | 同步和异步初始化，返回 InitializationResult |
| 实例工厂 | 使用共享资源和会话上下文创建独立 ILanguageEngine |
| 键盘布局 | 平台无关的按键标签、输出、功能动作及导航描述 |
| 自定义词条 | 接收规范化词条的添加、更新和删除，并推进相关词典版本 |
| 资源释放 | 回收该模块拥有的共享资源 |

显式传入的模块和工厂建立可静态引用的实现关系。语言标识与引擎标识分别比较；同一语言的多个实现由宿主配置选择。

## 会话入口

```csharp
/// <summary>
/// 接收统一输入操作并发布会话状态。
/// </summary>
public interface ISkySession : IDisposable
{
    SessionSnapshot Snapshot { get; }

    event Action<SessionSnapshot> StateChanged;
    event Action<OperationResult> OperationCompleted;
    event Action<SessionResult> Completed;

    OperationReceipt Process(InputOperation operation);
}
```

Process 在会话执行上下文中接收操作，返回已处理、等待处理或拒绝接纳的回执。每次接纳的操作取得会话内单调递增的标识。异步完成后通过 OperationCompleted 返回最终结果。

SerialExecutionContext 串行处理投递工作，适合独立托管宿主。PumpExecutionContext 固定创建线程，由宿主主循环调用 Drain；创建线程上的投递立即排空，回调中的新操作在当前工作结束后执行。

InputOperation 的工厂方法创建字符输入、组字回退、候选确认、导航、完成、取消、暂停和恢复等操作。候选确认显式提供查询标识与候选标识。写入结果未知时，NeedsResolution 状态保留输入；ResolveWrite 允许宿主明确再次提交或接受目标现状。

StateChanged 发布完整、不可变的当前快照。Completed 在会话结束时发布一次。事件通知发生在对应状态更新之后；通知接收者的异常单独报告，已完成的输入操作保持其结果。

| 类型 | 必需内容 |
| --- | --- |
| OperationReceipt | 操作标识、接纳状态、立即可得的结果或拒绝原因 |
| OperationResult | 操作标识、最终状态、编辑结果、相关诊断 |
| SessionSnapshot | 会话标识、活动语言和引擎、生命周期、运行状态、组字、候选、选中候选、编辑快照、待完成操作 |
| SessionResult | 结束原因（包括「运行时释放」）、最终正文快照、回填结果、截断信息、存档状态及诊断 |

会话快照区分生命周期和运行状态。打开中的会话可以处于活动、暂停或恢复过程；语言转换可以处于正常或字面输入状态；候选查询可以独立处于处理中。

## 统一输入操作

| 操作类别 | 参数与含义 |
| --- | --- |
| 字符输入 | 输入文本片段，交给活动引擎解释 |
| 组字回退 | 撤回一个有效组字步骤；组字为空时按正文退格 |
| 组字提交 | `CommitComposition()` 按当前转换规则提交组字，保留提交后的光标并继续编辑 |
| 候选导航 | 在当前查询版本的候选中移动选择 |
| 候选确认 | 查询版本与候选标识，确认当前有效候选 |
| 正文导航 | 方向、移动单位、是否扩展选区 |
| 正文编辑 | 向前删除、全选、清空、换行等动作 |
| 语言切换 | 会话配置列表中的目标语言，或按列表循环 |
| 会话设置 | 英语补全等当前会话内可调整设置 |
| 完成编辑 | 提交组字，执行所需回填并返回完成结果 |
| 取消编辑 | 根据编辑模式结束输入 |
| 重试转换 | 尝试恢复字面输入状态中的语言转换 |

设备按钮与这些操作的映射见[Unity 接入与默认表现](unity-integration.md)。核心使用操作语义调度，设备来源信息用于路由和诊断。

## 语言引擎入口

```csharp
/// <summary>
/// 处理语言组合、候选查询及组合状态恢复。
/// </summary>
public interface ILanguageEngine : IDisposable
{
    CompositionSnapshot Snapshot { get; }

    EngineStep Process(CompositionCommand command);

    Task<CandidatePage> QueryCandidatesAsync(
        CandidateQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// 创建包含组字和回退历史的不可变检查点。
    /// </summary>
    ICompositionCheckpoint CaptureCheckpoint();

    /// <summary>
    /// 从兼容的检查点恢复组合状态。
    /// </summary>
    ServiceResult RestoreCheckpoint(
        ICompositionCheckpoint checkpoint);
}
```

Process 同步产生一次语言状态变化。耗时查询通过 QueryCandidatesAsync 返回。EngineStep 携带新组字快照、查询需求、待提交片段及成功选择产生的词条记录；会话负责实际正文写入和词频处理。

QueryCandidatesAsync 只读取 CandidateQuery 携带的不可变数据和语言模块的只读资源，不访问引擎实例的可变状态。因此查询可以与同一实例上的 Process 并行执行，引擎实例无需加锁。查询所需的组字信息必须全部放入 CandidateQuery。

检查点包含引擎标识、状态格式版本、原始拼写、已选片段及回退历史。检查点在运行时内用于恢复，共享词典继续由模块持有。ServiceResult 提供成功状态、执行阶段、失败原因和取消状态。

## 组字与候选数据

| 数据 | 必需内容 |
| --- | --- |
| CompositionSnapshot | 组字版本、原始拼写、显示文本、已选片段、未转换范围、组合光标、可回退状态 |
| CandidateQuery | 查询标识、引擎实例代次、组字版本、词典版本、设置版本、查询所需的完整组字数据、匹配请求及分页信息 |
| CandidatePage | 原查询标识和版本、候选项、分页继续信息及查询结果 |
| Candidate | 候选标识、显示文本、输出文本、消耗的拼写范围、关联已有词条标识 |
| ChoiceRecord | 选择记录标识、所属操作、用户、语言、选中的已有词条及出现次数 |

CandidateQuery 固定 CompositionSnapshot、LanguageDataSnapshot 和用户词频副本。候选只消耗剩余拼写的前缀，以 ConsumedLength 表达范围。Candidate 的 Words 列出稳定词条身份和出现次数。EngineStep 提供最新组字、可选提交文本、查询需求和本次选择的词条；提交文本经目标接受后，由会话发送 Clear 命令结束组字。

候选标识在所属查询内解释。接纳异步结果时，运行时核对会话、实例代次和查询版本；候选确认使用当前已经显示的有效结果。

整句候选可关联一个已有整句词条，也可关联按顺序构成句子的已有词条。选择记录保留实际选择的词条身份，供词频服务统计。

## 文本目标入口

```csharp
/// <summary>
/// 提供正文、选区、编辑和光标导航能力。
/// </summary>
public interface ITextTarget
{
    /// <summary>
    /// 宿主控件的稳定引用身份，用于跨运行时仲裁编辑权。
    /// </summary>
    object Identity { get; }

    TextSnapshot ReadSnapshot();

    TextEditResult ApplyEdit(TextEditRequest request);

    TextNavigationResult Navigate(TextNavigationRequest request);

    /// <summary>
    /// 上报外部正文修改和目标失效。
    /// </summary>
    event Action<TextTargetChange> Changed;
}
```

| 类型 | 必需内容与规则 |
| --- | --- |
| TextSnapshot | 完整正文、选区锚点、活动端、单调递增的目标版本 |
| TextTargetChange | 变化种类（外部正文修改或目标失效）；外部正文修改时附最新目标快照 |
| TextEditRequest | 会话与操作标识、预期目标版本、替换范围、插入文本、编辑种类 |
| TextEditResult | 接受、截断、拒绝、版本冲突或目标失效；实际应用文本和最新快照 |
| TextNavigationRequest | 会话与操作标识、预期版本、水平或垂直方向、移动单位、扩展选区标志 |
| TextNavigationResult | 导航结果、最新选区和目标快照 |

文本位置使用 UTF-16 偏移，范围使用起点与长度。锚点和活动端分别保存，保留选区方向。目标适配器负责与控件坐标转换，并把操作边界调整到完整 Unicode 字符边界。

同一宿主控件的所有适配器返回同一个 Identity 引用。编辑权在进程内按该身份仲裁。文本编辑通过操作标识和操作内编辑序号去重，导航通过操作标识去重。实现 ITextOperationCache 的目标在会话结束后由运行时调用 ReleaseSession，释放该会话的回执。

核心提供 MemoryTextTarget，用于独立宿主和草稿正文。其长度上限采用 UTF-16 单元，零表示不限长度；原框插入保留未选中的正文，完整回填截取全文前缀。内存目标按逻辑行执行垂直导航，平台目标按实际排版执行。

适配器在正文或选区发生有效变化时推进版本。预期版本不匹配时返回最新快照，由会话按外部修改规则处理。垂直导航基于控件的实际行排版。

适配器根据操作归属区分 SKY 自己的写入和宿主修改。检测到不属于 SKY 操作的正文变化，或目标失效（例如控件被销毁）时，触发 Changed。会话在自身执行上下文中处理该事件，并按外部变化规则结束会话。

ApplyEdit 返回控件实际接受的内容。长度截断到零仍是有确定结果的截断操作；普通验证拒绝具有独立状态。细节见[会话与文本编辑](session-and-editing.md)。

## 词频与存档契约

词频服务接收 ChoiceRecord，按用户和语言累计，向引擎提供只读使用信息。词频服务不属于某个运行时实例，而是在进程内按用户标识共享：同一用户在所有实例中的选择累计到同一份数据，存档修订号统一递增。

存档 Provider 在进程内只配置一次：宿主在创建第一个实例前设置，或由第一个实例的 RuntimeOptions 带入，之后的实例沿用。Provider 由宿主创建和释放。

存档 Provider 读写结构化 FrequencySnapshot：

| 字段 | 内容 |
| --- | --- |
| 用户标识 | 存档归属 |
| 语言标识 | 词频命名范围 |
| 数据格式版本 | Provider 数据迁移和读取兼容依据 |
| 存档修订号 | 一次完整保存所对应的内存数据版本 |
| 词条计数集合 | 稳定词条标识及累计选择次数 |

Provider 提供同步读取、同步写入、异步读取和异步写入，返回成功、失败或取消及必要诊断。写入语义为保存指定修订的完整数据，具体序列化与存储位置由 Provider 决定。

同一用户和语言的写入按修订顺序执行。保存中的新增选择保留在内存的新修订中，较早写入成功只确认其包含的修订。

## 结果和诊断

正常验证失败、目标占用、初始化失败、查询失败和存档失败均通过对应结果表达。诊断至少包含模块、阶段、会话或操作关联、可读原因和恢复状态。

错误恢复不会重新发布已完成操作的业务完成通知。结果未知的外部写入通过重新读取和明确的处理状态解决，恢复契约见[恢复与词频存储](recovery-and-persistence.md)。
