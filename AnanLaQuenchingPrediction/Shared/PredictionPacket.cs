using ProtoBuf;

namespace AnanLaQuenchingPrediction.Shared
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PredictionPacket
    {
        /// <summary>物品即将碎裂的预警消息。</summary>
        public string WarningMessage { get; set; }
    }
}
