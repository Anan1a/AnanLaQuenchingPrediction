using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace AnanLaQuenchingPrediction
{
    public class AnanLaQuenchingPredictionModSystem : ModSystem
    {
        // Called on server and client
        public override void Start(ICoreAPI api)
        {
            Mod.Logger.Notification("Hello from AnanLa QuenchingPrediction mod: " + Lang.Get("ananlaquenchingprediction:hello"));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            Mod.Logger.Notification("Hello from AnanLa QuenchingPrediction mod server side");
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            Mod.Logger.Notification("Hello from AnanLa QuenchingPrediction mod client side");
        }
    }
}
