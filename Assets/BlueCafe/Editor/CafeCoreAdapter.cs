using UnityEngine;

namespace BlueCafe
{
    // The retained Editor API and standalone session use the same placement validator.
    internal static class CafeCoreAdapter
    {
        static CafeCatalog sourceCatalog;
        static Core.Catalog coreCatalog;
        internal static Core.Catalog Catalog()
        {
            if (!ReferenceEquals(sourceCatalog, CafeApi.Catalog)) { sourceCatalog = CafeApi.Catalog; coreCatalog = JsonUtility.FromJson<Core.Catalog>(JsonUtility.ToJson(sourceCatalog)); }
            return coreCatalog;
        }
        internal static void ValidateRoom(CafeRoom room)
        {
            try { Core.Placement.ValidateRoom(JsonUtility.FromJson<Core.Room>(JsonUtility.ToJson(room))); }
            catch (Core.ValidationException e) { throw new CafeValidationException(e.code, e.Message); }
        }
        internal static Vector3 Constrain(CafeItem item, Vector3 position, int turns, CafeRoom room)
        {
            try
            {
                var p = Core.Placement.Constrain(Catalog(), JsonUtility.FromJson<Core.Item>(JsonUtility.ToJson(item)), new Core.Position(position.x, position.y, position.z), turns, JsonUtility.FromJson<Core.Room>(JsonUtility.ToJson(room)));
                return new Vector3(p.x, p.y, p.z);
            }
            catch (Core.ValidationException e) { throw new CafeValidationException(e.code, e.Message); }
        }
        internal static void ValidateLayout(CafeLayout layout)
        {
            try { Core.Placement.ValidateLayout(Catalog(), JsonUtility.FromJson<Core.Layout>(JsonUtility.ToJson(layout))); }
            catch (Core.ValidationException e) { throw new CafeValidationException(e.code, e.Message); }
        }
    }
}
