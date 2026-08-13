namespace AnanLaQuenchingPrediction.Config
{
    /// <summary>服务端配置，控制淬火预警宽限窗口。</summary>
    public class ServerConfig
    {
        /// <summary>预警宽限窗口（毫秒）：必碎确定后该时长内跳过碎裂判断，供玩家反应捞出。默认 500（≈10 刻 @20tps）。</summary>
        public long GraceWindowMs { get; set; } = 500;
    }
}
