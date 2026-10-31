namespace Mods.ModManager.NewModTest
{
    using System.Linq;
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Mounts <see cref="NewModTestDriver"/> for <c>--itest=new_mod</c> only: a frontend run, no game mods loaded.</summary>
    public class NewModTestInitializer : IModInitializer
    {
        private static bool queued;

        public void OnModLoaded(string modId) { }

        public void OnModResourcesReady()
        {
            if (queued || !OS.GetCmdlineUserArgs().Contains("--itest=new_mod"))
                return;
            queued = true;
            ModRoot.AddDeferred(new NewModTestDriver());
        }
    }
}
