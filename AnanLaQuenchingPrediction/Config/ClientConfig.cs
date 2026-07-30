using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AnanLaQuenchingPrediction.Config
{
    /// <summary>消息显示位置/风格。</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MessageDisplayMode
    {
        /// <summary>底部提示栏：红色震动文字，居中显示。</summary>
        BottomError = 0,
        /// <summary>聊天栏：左下角淡入显示。</summary>
        Chat = 1,
        /// <summary>中央发现动画：居中淡入淡出。</summary>
        Discovery = 2,
    }

    /// <summary>客户端配置，仅控制消息显示方式。</summary>
    public class ClientConfig
    {
        /// <summary>是否显示淬火预警提示。</summary>
        public bool PredictionPrompt { get; set; } = true;
        /// <summary>消息显示风格，可选 BottomError / Chat / Discovery。</summary>
        public MessageDisplayMode DisplayMode { get; set; } = MessageDisplayMode.BottomError;
    }
}
