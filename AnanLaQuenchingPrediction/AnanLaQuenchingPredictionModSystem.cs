using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using AnanLaQuenchingPrediction.Harmony;
using AnanLaQuenchingPrediction.Shared;

namespace AnanLaQuenchingPrediction
{
    /// <summary>模组主入口，负责网络通道注册和 Harmony 补丁安装。</summary>
    public class AnanLaQuenchingPredictionModSystem : ModSystem
    {
        private ICoreClientAPI clientApi;

        /// <summary>
        /// 服务端初始化入口。注册网络通道、安装 Harmony 补丁。
        /// </summary>
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            // ── 注册服务端网络通道 ──
            QuenchingPredictionPatches.NotificationChannel = api.Network.RegisterChannel("quenchPredict")
                .RegisterMessageType<PredictionPacket>();

            // ── 向 Harmony 补丁注入依赖 ──
            QuenchingPredictionPatches.ServerApi = api;

            // ═══ Harmony 补丁（显式 Patch 避免 PatchAll 对 private 方法的兼容性问题）═══
            try
            {
                var harmony = new HarmonyLib.Harmony("ananlaquenchingprediction");
                var quenchType = typeof(CollectibleBehaviorQuenchable);

                // 1. 补丁 IsGettingCooled Postfix — 检测 willbreak 并预警
                var isGettingCooledMethod = quenchType.GetMethod("IsGettingCooled",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (isGettingCooledMethod != null)
                {
                    var postfix = new HarmonyMethod(typeof(QuenchingPredictionPatches)
                        .GetMethod(nameof(QuenchingPredictionPatches.IsGettingCooledPostfix),
                            BindingFlags.Static | BindingFlags.Public));
                    harmony.Patch(isGettingCooledMethod, postfix: postfix);
                }
                else
                {
                    api.Logger.Warning("[Init] 未找到 IsGettingCooled，预警将失效。游戏版本可能已变更。");
                }

                // 2. 补丁 trySettleWorkItem Postfix — 淬火成功时清理临时标记
                var trySettleMethod = quenchType.GetMethod("trySettleWorkItem",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (trySettleMethod != null)
                {
                    var postfix = new HarmonyMethod(typeof(QuenchingPredictionPatches)
                        .GetMethod(nameof(QuenchingPredictionPatches.TrySettleWorkItemPostfix),
                            BindingFlags.Static | BindingFlags.Public));
                    harmony.Patch(trySettleMethod, postfix: postfix);
                }
                else
                {
                    api.Logger.Warning("[Init] 未找到 trySettleWorkItem，标记清理将失效。");
                }
            }
            catch (Exception ex)
            {
                api.Logger.Error("[Init] Harmony 补丁失败: {0}", ex);
            }
        }

        /// <summary>
        /// 客户端初始化入口。注册网络通道及消息处理器。
        /// </summary>
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            clientApi = api;

            // ── 注册客户端网络通道并绑定消息处理器 ──
            api.Network.RegisterChannel("quenchPredict")
                .RegisterMessageType<PredictionPacket>()
                .SetMessageHandler<PredictionPacket>(OnPrediction);
        }

        /// <summary>
        /// 处理服务端推送的淬火预警包，在客户端显示警告。
        /// </summary>
        private void OnPrediction(PredictionPacket packet)
        {
            string msg = Lang.Get("ananlaquenchingprediction:" + packet.WarningMessage);
            clientApi.TriggerIngameError(clientApi, "quench_break_warning", msg);
        }

        /// <summary>
        /// 模组卸载时清理 Harmony 补丁。
        /// </summary>
        public override void Dispose()
        {
            try
            {
                var harmony = new HarmonyLib.Harmony("ananlaquenchingprediction");
                harmony.UnpatchAll("ananlaquenchingprediction");
            }
            catch { }
            base.Dispose();
        }
    }
}
