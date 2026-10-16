using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity;
using UnityEngine;

namespace SkyKeyYuyan.Validation
{
    internal static class SkyFrequencyPlayerCheck
    {
        [Serializable]
        private sealed class Header { public int parts; public long revision; }
        private const string KeyPrefix = "SkyKeyYuyan.PlayerValidation.Storage";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => SkyPlayerValidation.Register("PlayerPrefs 词频读写与性能", Run);
        private static Task Run()
        {
            var store = new PlayerPrefsFrequencyStore(new PumpExecutionContext(), KeyPrefix);
            foreach (int size in new[] { 100, 1000, 10000 })
            {
                string user = "size-" + size;
                var counts = Enumerable.Range(0, size).ToDictionary(index => "word-" + index, index => (long)index + 1);
                var saves = new double[5]; var loads = new double[5];
                try
                {
                    for (int iteration = 0; iteration < saves.Length; iteration++)
                    {
                        var snapshot = new FrequencySnapshot(user, "zh-Hans", iteration + 1, counts);
                        var timer = Stopwatch.StartNew(); var saved = store.Save(snapshot); saves[iteration] = timer.Elapsed.TotalMilliseconds;
                        SkyPlayerValidation.Require(saved.Result.Succeeded, saved.Result.Reason);
                        timer.Restart(); var loaded = store.Load(user, "zh-Hans"); loads[iteration] = timer.Elapsed.TotalMilliseconds;
                        SkyPlayerValidation.Require(loaded.Result.Succeeded && loaded.Snapshot != null && loaded.Snapshot.Revision == snapshot.Revision && loaded.Snapshot.Counts.Count == size && loaded.Snapshot.Counts["word-" + (size - 1)] == size, "PlayerPrefs 词频读写结果错误");
                    }
                    SkyPlayerValidation.RecordMeasurement("playerprefs/save-main-thread", size, saves);
                    SkyPlayerValidation.RecordMeasurement("playerprefs/load-main-thread", size, loads);
                }
                finally { Clear(user); }
            }
            return Task.CompletedTask;
        }
        private static void Clear(string user)
        {
            string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string prefix = KeyPrefix + "." + Encode(user) + "." + Encode("zh-Hans");
            if (!PlayerPrefs.HasKey(prefix + ".head")) return;
            var header = JsonUtility.FromJson<Header>(PlayerPrefs.GetString(prefix + ".head"));
            for (int part = 0; part < header.parts; part++) PlayerPrefs.DeleteKey(prefix + "." + header.revision + "." + part);
            PlayerPrefs.DeleteKey(prefix + ".head"); PlayerPrefs.Save();
        }
    }
}
