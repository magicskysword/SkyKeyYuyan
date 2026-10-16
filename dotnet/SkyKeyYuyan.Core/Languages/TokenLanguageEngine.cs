namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 将词内输入和分隔符拆成同一操作中的连续处理步骤。
    /// </summary>
    public abstract class TokenLanguageEngine : HistoryLanguageEngine
    {
        /// <summary>
        /// 创建具有词边界的语言引擎。
        /// </summary>
        protected TokenLanguageEngine(string engineId) : base(engineId) { }
        /// <summary>
        /// 判断字符是否属于当前语言的词内拼写。
        /// </summary>
        protected abstract bool IsCompositionCharacter(char value);
        /// <inheritdoc />
        public override EngineStep Process(CompositionCommand command)
        {
            if (command.Kind != "input" || command.Text.Length == 0) return base.Process(command);
            bool composing = IsCompositionCharacter(command.Text[0]);
            if (!composing && !Snapshot.IsEmpty) return new EngineStep(Snapshot, deferredInput: command.Text);
            int end = 0;
            while (end < command.Text.Length && IsCompositionCharacter(command.Text[end]) == composing) end++;
            var step = base.Process(CompositionCommand.Insert(command.Text.Substring(0, end)));
            return new EngineStep(step.Snapshot, composing ? step.CommitText : step.Snapshot.DisplayText, composing && step.QueryRequested, step.Choices, end < command.Text.Length ? command.Text.Substring(end) : null);
        }
    }
}
