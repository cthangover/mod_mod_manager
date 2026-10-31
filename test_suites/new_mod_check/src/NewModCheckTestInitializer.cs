namespace Mods.ModManager.NewModTest
{
    using System.Linq;
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Mounts <see cref="NewModCheckTestDriver"/> for <c>--itest=new_mod_check</c> (a game run of the fixture).</summary>
    public class NewModCheckTestInitializer : IModInitializer
    {
        private static bool queued;

        public void OnModLoaded(string modId) { }

        public void OnModResourcesReady()
        {
            if (queued || !OS.GetCmdlineUserArgs().Contains("--itest=new_mod_check"))
                return;
            queued = true;
            ModRoot.AddDeferred(new NewModCheckTestDriver());
        }
    }
}
