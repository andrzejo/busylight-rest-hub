using BusylightRestHost.Utils;

namespace BusylightRestHost.Action
{
    public class VersionAction : Action
    {
        private readonly string _version;

        public VersionAction(string overridenVersion = null) : base(null, null)
        {
            _version = overridenVersion ?? Version.Get();
        }

        public override string Execute()
        {
            return Json.Serialize(typeof(VersionTo), new VersionTo(_version));
        }
    }
}