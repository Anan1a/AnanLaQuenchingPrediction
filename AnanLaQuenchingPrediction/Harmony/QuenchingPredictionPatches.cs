using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using AnanLaQuenchingPrediction.Shared;

namespace AnanLaQuenchingPrediction.Harmony
{
    public static class QuenchingPredictionPatches
    {
        /// <summary>服务端 API 引用，在 ModSystem.StartServerSide 中赋值。</summary>
        internal static ICoreServerAPI ServerApi { get; set; }

        /// <summary>服务端→客户端的网络通道。</summary>
        internal static IServerNetworkChannel NotificationChannel { get; set; }

        /// <summary>发送预测通知包。通道未注册或非服务端玩家时静默跳过。</summary>
        private static void SendWarning(IPlayer player, PredictionEventType eventType)
        {
            if (player is not IServerPlayer serverPlayer) return;
            NotificationChannel?.SendPacket(new PredictionPacket
            {
                EventType = eventType
            }, serverPlayer);
        }

        /// <summary>
        /// 原方法 v1.22.3: <c>private void IsGettingCooled(IWorldAccessor world, ItemSlot slot, Vec3d pos, float dt, float temperature)</c>
        /// 每帧执行淬火冷却逻辑，在达到碎裂条件时设置 willbreak=true。
        /// 此 Postfix 在之后检测 willbreak 并发送预警。
        /// </summary>
        /// <remarks>Harmony Postfix — 补丁目标: <see cref="CollectibleBehaviorQuenchable"/>.IsGettingCooled</remarks>
        /// <param name="world">游戏世界访问器。</param>
        /// <param name="slot">当前淬火物品所在的物品槽。</param>
        public static void IsGettingCooledPostfix(IWorldAccessor world, ItemSlot slot)
        {
            try
            {
                // ── 守卫条件 ──
                // 最高频：大多数帧物品正常冷却
                if (slot.Empty || slot.Itemstack == null) return;
                // willbreak 仅由服务端原方法设置，客户端原方法直接 return
                if (world.Side == EnumAppSide.Client) return;

                var stack = slot.Itemstack;

                // 次高频：已预警过的物品每帧命中，避免重复查 willbreak
                if (stack.TempAttributes.GetBool("breakPredictionWarned")) return;

                // 检测 willbreak：true 表示物品即将碎裂
                if (!stack.TempAttributes.GetBool("willbreak")) return;
                stack.TempAttributes.SetBool("breakPredictionWarned", true);

                // ── 通过槽位回溯所属玩家 ──
                var player = FindPlayerFromSlot(slot);
                if (player == null) return;

                // ── 发送预警通知 ──
                SendWarning(player, PredictionEventType.BreakWarning);
            }
            catch (Exception ex)
            {
                ServerApi?.Logger.Error("[PredictionPatches] IsGettingCooledPostfix 异常: {0}", ex);
            }
        }

        /// <summary>
        /// 原方法 v1.22.3: <c>private void trySettleWorkItem(IWorldAccessor world, ItemStack itemstack, float temperature, string currentState)</c>
        /// 淬火完成（物品存活）时触发，清理物品上的预测标记。
        /// </summary>
        /// <remarks>Harmony Postfix — 补丁目标: <see cref="CollectibleBehaviorQuenchable"/>.trySettleWorkItem</remarks>
        /// <param name="__instance">目标类实例，访问原方法所在对象的成员。</param>
        /// <param name="itemstack">淬火完成的物品堆。</param>
        /// <param name="currentState">淬火状态 (<c>"quench"</c>/<c>"overheat"</c>)。</param>
        public static void TrySettleWorkItemPostfix(CollectibleBehaviorQuenchable __instance, IWorldAccessor world, ItemStack itemstack, string currentState)
        {
            try
            {
                if (currentState != "quench") return;

                // 确认淬火已真正完成
                string newState = __instance.GetState(itemstack);
                if (newState != "settled") return;

                // 清理临时标记，避免残留影响后续淬火
                itemstack.TempAttributes.RemoveAttribute("breakPredictionWarned");
            }
            catch (Exception ex)
            {
                ServerApi?.Logger.Error("[PredictionPatches] TrySettleWorkItemPostfix 异常: {0}", ex);
            }
        }

        /// <summary>
        /// 通过槽位的 Inventory 引用直接获取所属玩家，无需遍历。
        /// </summary>
        private static IPlayer FindPlayerFromSlot(ItemSlot slot)
        {
            return slot.Inventory is InventoryBasePlayer playerInv ? playerInv.Player : null;
        }
    }
}
