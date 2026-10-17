using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.Unity;
using UnityEngine;
using UnityEngine.TestTools;

public class UnityServicesTests
{
    [Test]
    public void PackagedTextAssetsAreAvailableWithoutFileSystemPaths()
    {
        var bytes = SkyUnityServices.Shared.Resources.Read("chinese/lexicon.bytes");
        Assert.That(Encoding.UTF8.GetString(bytes, 0, 40), Does.StartWith("SKY\t1\tzh-Hans\t"));
    }

    [TestCase("chinese", "zh-Hans")]
    [TestCase("english", "en")]
    [TestCase("korean", "ko")]
    public void PackagedKeyboardResourcesHaveNavigationAndLanguageOutputs(string directory, string language)
    {
        var layout = KeyboardLayout.Parse(SkyUnityServices.Shared.Resources.Read(directory + "/keyboard.bytes"), language);
        Assert.That(layout.Keys.Count, Is.GreaterThan(50));
        Assert.That(layout["space"].Output, Is.EqualTo(" "));
        Assert.That(layout.Navigate("key-1-9", TextNavigation.Down), Is.EqualTo("semicolon"));
        Assert.That(layout.Navigate("key-1-0", TextNavigation.Down), Is.EqualTo("key-2-0"));
        Assert.That(layout.Navigate("key-2-0", TextNavigation.Down), Is.EqualTo("key-3-0"));
        Assert.That(layout["key-1-0"].Output, Is.EqualTo(language == "ko" ? "ㅂ" : "q"));
        Assert.That(layout["key-1-0"].Alternate, Is.EqualTo(language == "ko" ? "ㅃ" : "Q"));
    }

    [UnityTest]
    public IEnumerator AsynchronousLanguageInitializationKeepsUnityResourceCallsOnMainThread()
    {
        using (var module = new ChinesePinyinModule())
        {
            var initialized = module.InitializeAsync(SkyUnityServices.Shared.Resources, CancellationToken.None);
            float deadline = Time.realtimeSinceStartup + 30;
            while (!initialized.IsCompleted && Time.realtimeSinceStartup < deadline) { SkyUnityServices.Shared.Context.Drain(); yield return null; }
            Assert.That(initialized.IsCompleted, Is.True);
            Assert.That(initialized.Result.Succeeded, Is.True, initialized.Result.Reason);
            Assert.That(module.CaptureData().Words.Count, Is.GreaterThan(200000));
        }
    }

    [UnityTest]
    public IEnumerator PlayerPrefsRoundTripKeepsUnicodeChunksAndLongCounts()
    {
        string prefix = "SkyKeyYuyan.Tests." + Guid.NewGuid().ToString("N");
        var store = new PlayerPrefsFrequencyStore(SkyUnityServices.Shared.Context, prefix);
        var counts = new Dictionary<string, long>();
        for (int index = 0; index < 1000; index++) counts.Add("词条😀" + index, index * 10000000000L);
        counts.Add("large", long.MaxValue - 5);
        var snapshot = new FrequencySnapshot("test-user", "test-language", 1, counts);
        try
        {
            var saving = Task.Run(() => store.SaveAsync(snapshot, CancellationToken.None));
            while (!saving.IsCompleted) { SkyUnityServices.Shared.Context.Drain(); yield return null; }
            Assert.That(saving.Result.Result.Succeeded, Is.True, saving.Result.Result.Reason);
            var loading = Task.Run(() => store.LoadAsync("test-user", "test-language", CancellationToken.None));
            while (!loading.IsCompleted) { SkyUnityServices.Shared.Context.Drain(); yield return null; }
            Assert.That(loading.Result.Result.Succeeded, Is.True, loading.Result.Result.Reason);
            Assert.That(loading.Result.Snapshot.Counts, Is.EquivalentTo(counts));
        }
        finally
        {
            string key = StorageKey(prefix);
            PlayerPrefs.DeleteKey(key + ".head");
            for (int part = 0; part < 64; part++) PlayerPrefs.DeleteKey(key + ".1." + part);
            PlayerPrefs.Save();
        }
    }

    [Test]
    public void UnreadablePreviousHeaderCanBeOverwrittenByNewSnapshot()
    {
        string prefix = "SkyKeyYuyan.Tests." + Guid.NewGuid().ToString("N");
        string key = StorageKey(prefix);
        var store = new PlayerPrefsFrequencyStore(SkyUnityServices.Shared.Context, prefix);
        try
        {
            PlayerPrefs.SetString(key + ".head", "invalid");
            var result = store.Save(new FrequencySnapshot("test-user", "test-language", 2, new Dictionary<string, long> { ["word"] = 1 }));
            Assert.That(result.Result.Succeeded, Is.True, result.Result.Reason);
            Assert.That(store.Load("test-user", "test-language").Snapshot.Counts["word"], Is.EqualTo(1));
        }
        finally { PlayerPrefs.DeleteKey(key + ".head"); PlayerPrefs.DeleteKey(key + ".2.0"); PlayerPrefs.Save(); }
    }
    private static string StorageKey(string prefix)
    {
        Func<string, string> encode = value => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return prefix + "." + encode("test-user") + "." + encode("test-language");
    }
}
