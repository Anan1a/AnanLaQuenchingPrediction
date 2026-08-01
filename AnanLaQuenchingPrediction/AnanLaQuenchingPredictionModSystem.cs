using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using AnanLaQuenchingPrediction.Config;
using AnanLaQuenchingPrediction.Harmony;
using AnanLaQuenchingPrediction.Shared;

namespace AnanLaQuenchingPrediction
{
    /// <summary>模组主入口，负责网络通道注册和 Harmony 补丁安装。</summary>
    public class AnanLaQuenchingPredictionModSystem : ModSystem
    {
        private ICoreClientAPI clientApi;
        /// <summary>客户端缓存的配置，决定提示显示方式和渲染风格。</summary>
        private ClientConfig clientConfig;
        /// <summary>姊妹模组（淬火保底）是否已安装，决定是否启用伪同屏协议。</summary>
        private bool guaranteeModPresent;

        /// <summary>本模组 modid（与 modinfo.json 一致）。</summary>
        private const string ModId = "ananlaquenchingprediction";
        /// <summary>姊妹模组（淬火保底）modid。</summary>
        private const string GuaranteeModId = "ananlaquenchingguarantee";
        /// <summary>语言键前缀。</summary>
        private const string LangPrefix = ModId + ":";
        /// <summary>伪同屏协议共享缓存键（由淬火保底写入，跟随其命名空间）。</summary>
        private const string QuenchLastMsgKey = GuaranteeModId + ":quenchLastMsg";
        /// <summary>客户端配置文件。</summary>
        private const string ClientConfigFile = ModId + "_client_config.json";

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
                var harmony = new HarmonyLib.Harmony(ModId);
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

            // ── 加载客户端本地配置 ──
            // PredictionPrompt、DisplayMode 由客户端本地控制，不与服务端同步
            clientConfig = api.LoadModConfig<ClientConfig>(ClientConfigFile);
            clientConfig ??= new ClientConfig();
            api.StoreModConfig(clientConfig, ClientConfigFile);

            // ── 检测姊妹模组（淬火保底），仅在共存时启用伪同屏协议 ──
            guaranteeModPresent = api.ModLoader.IsModEnabled(GuaranteeModId);

            // ── 注册客户端网络通道并绑定消息处理器 ──
            api.Network.RegisterChannel("quenchPredict")
                .RegisterMessageType<PredictionPacket>()
                .SetMessageHandler<PredictionPacket>(OnPrediction);
        }

        /// <summary>
        /// 处理服务端推送的淬火预警包，根据事件类型选择语言模板渲染。
        /// </summary>
        private void OnPrediction(PredictionPacket packet)
        {
            if (!clientConfig.PredictionPrompt) return;

            string msg = packet.EventType switch
            {
                PredictionEventType.BreakWarning => Lang.Get(LangPrefix + "break_warning"),
                _ => null
            };
            if (msg == null) return;

            // ── 伪同屏协议：仅在姊妹模组（淬火保底）共存时读取并消费保底侧待合并消息 ──
            // 保底侧仅单槽显示模式（非聊天栏）才写入，聊天栏天然累积无需合并
            if (guaranteeModPresent &&
                clientApi.ObjectCache.Remove(QuenchLastMsgKey, out object prev) &&
                prev is (string prevMsg, int prevMode) &&
                prevMode == (int)clientConfig.DisplayMode)
            {
                msg = prevMsg + "\n" + msg;
            }

            // 根据本地 DisplayMode 配置决定渲染方式
            switch (clientConfig.DisplayMode)
            {
                case Config.MessageDisplayMode.Chat:
                    // 聊天栏：左下角淡入显示
                    clientApi.ShowChatMessage(msg);
                    break;
                case Config.MessageDisplayMode.Discovery:
                    // 中央发现动画：居中淡入淡出
                    clientApi.TriggerIngameDiscovery(clientApi, "quench_break_warning", msg);
                    break;
                default:
                    // 底部提示栏：红色震动文字，居中显示（默认行为）
                    clientApi.TriggerIngameError(clientApi, "quench_break_warning", msg);
                    break;
            }
        }

        /// <summary>
        /// 模组卸载时清理 Harmony 补丁。
        /// </summary>
        public override void Dispose()
        {
            try
            {
                var harmony = new HarmonyLib.Harmony(ModId);
                harmony.UnpatchAll(ModId);
            }
            catch { }
            base.Dispose();
        }
    }
}
