using BlueCafe.Core;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        void SetDefaultCafe(string cafeId)
        {
            var layout = store.LoadCafe(cafeId);
            Placement.ValidateLayout(Pack.Catalog, layout, true);
            var settings = store.LoadSettings(); settings.defaultCafeId = cafeId;
            store.SaveSettings(settings);
            Message(layout.name + " will open at startup.");
        }
    }
}
