using System.Collections.Generic;
using UnityEngine;

namespace BlockWorldMVP
{
    [CreateAssetMenu(fileName = "BlockCatalog", menuName = "Block World MVP/Block Catalog")]
    public class BlockCatalog : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public string id;
            public GameObject prefab;
        }

        [SerializeField]
        private List<Entry> entries = new List<Entry>();

        private Dictionary<string, GameObject> _map;

        public bool TryGetPrefab(string id, out GameObject prefab)
        {
            EnsureMapBuilt();
            return _map.TryGetValue(id, out prefab);
        }

        private void EnsureMapBuilt()
        {
            if (_map != null)
            {
                return;
            }

            _map = new Dictionary<string, GameObject>();
            foreach (Entry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.prefab == null)
                {
                    continue;
                }

                if (!_map.ContainsKey(entry.id))
                {
                    _map.Add(entry.id, entry.prefab);
                }
            }
        }

        private void OnValidate()
        {
            _map = null;
        }
    }
}
