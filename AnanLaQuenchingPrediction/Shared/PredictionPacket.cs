using ProtoBuf;

namespace AnanLaQuenchingPrediction.Shared
{
    /// <summary>淬火预测事件类型。</summary>
    public enum PredictionEventType
    {
        /// <summary>物品即将在淬火中碎裂。</summary>
        BreakWarning = 0,
    }

    /// <summary>
    /// 服务端→客户端的预测通知包，携带事件类型，
    /// 由客户端根据 <see cref="EventType"/> 选择对应语言模板渲染。
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PredictionPacket
    {
        /// <summary>预测事件类型，客户端据此选择语言键。</summary>
        public PredictionEventType EventType { get; set; }
    }
}
