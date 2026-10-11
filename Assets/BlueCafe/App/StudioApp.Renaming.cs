using System;
using BlueCafe.Core;
using BlueCafe.Infrastructure;

namespace BlueCafe.App
{
    public sealed partial class StudioApp
    {
        CafeRecord RenameSavedPreset(string cafeId, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new Exception("Choose a cafe name.");
            name = name.Trim();
            bool active = savedCafeId == cafeId, wasDirty = Dirty;
            var candidate = active ? Session.Layout : null;
            if (active) { candidate.name = name; Placement.ValidateLayout(Pack.Catalog, candidate, true); }
            var record = store.RenameCafe(cafeId, name);
            if (active)
            {
                var result = Session.Replace(candidate, Session.Revision);
                if (!result.success) throw new Exception(result.message);
                if (!wasDirty) Session.MarkSaved();
                Refresh();
            }
            Message("Renamed " + record.name + ".");
            return record;
        }
    }
}
