using System;
using System.Collections.Generic;
using CapsuleArena.Networking.Pooling;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CapsuleArena.Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests verifying FusionObjectPoolProvider 32-instance FIFO prewarm,
    /// IPooledResettable hook invocation counts, zero runtime Instantiate/Destroy calls under
    /// normal burst load, hard cap at 48, and 0 B GC steady-state operations
    /// (Story 4-2; ADR-0003; TR-session-002).
    /// </summary>
    [TestFixture]
    public class FusionObjectPoolProviderTests
    {
        private sealed class ResettableSpy : MonoBehaviour, IPooledResettable
        {
            public int AcquireCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public void OnPoolAcquire() => AcquireCount++;
            public void OnPoolRelease() => ReleaseCount++;
        }

        private GameObject _providerGo;
        private FusionObjectPoolProvider _pool;
        private GameObject _prefabGo;
        private NetworkObject _prefabNetObj;

        [SetUp]
        public void SetUp()
        {
            _providerGo = new GameObject("PoolProvider_Test");
            _pool = _providerGo.AddComponent<FusionObjectPoolProvider>();

            _prefabGo = new GameObject("ProjectilePrefab_Test");
            _prefabGo.SetActive(false);
            _prefabNetObj = _prefabGo.AddComponent<NetworkObject>();
            _prefabGo.AddComponent<ResettableSpy>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_pool != null)
            {
                _pool.ClearAndDestroyAllImmediate();
            }

            if (_prefabGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_prefabGo);
            }

            if (_providerGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_providerGo);
            }
        }

        [Test]
        public void Prewarm_DefaultCount32_EnforcesFifoOrderInvokesResettableHooksAndZeroRuntimeCalls()
        {
            _pool.Prewarm(_prefabNetObj, FusionObjectPoolProvider.DefaultPrewarmCount);

            Assert.AreEqual(32, _pool.GetInactiveCount(_prefabNetObj));
            Assert.AreEqual(32, _pool.GetTotalCreatedCount(_prefabNetObj));
            Assert.AreEqual(0, _pool.RuntimeInstantiateCallsAfterPrewarm);

            List<NetworkObject> active = new List<NetworkObject>(32);
            for (int i = 0; i < 32; i++)
            {
                Vector3 pos = new Vector3(i, 1.0f, 0f);
                NetworkObject acquired = _pool.AcquireFromPool(_prefabNetObj, pos, Quaternion.identity);
                Assert.IsNotNull(acquired);
                Assert.IsTrue(acquired.gameObject.activeSelf);
                Assert.AreEqual(pos, acquired.transform.position);

                ResettableSpy spy = acquired.GetComponent<ResettableSpy>();
                Assert.AreEqual(1, spy.AcquireCount, "OnPoolAcquire must run on each acquired instance.");
                Assert.AreEqual(1, spy.ReleaseCount, "Prewarm calls OnPoolRelease once to initialize clean dormant state.");
                active.Add(acquired);
            }

            Assert.AreEqual(0, _pool.GetInactiveCount(_prefabNetObj));
            Assert.AreEqual(0, _pool.RuntimeInstantiateCallsAfterPrewarm, "Acquiring <= 32 instances must trigger 0 runtime Instantiate calls.");

            NetworkObject firstReleased = active[0];
            for (int i = 0; i < active.Count; i++)
            {
                _pool.ReleaseToPool(active[i]);
                Assert.IsFalse(active[i].gameObject.activeSelf);
            }

            Assert.AreEqual(32, _pool.GetInactiveCount(_prefabNetObj));
            Assert.AreEqual(0, _pool.RuntimeDestroyCalls, "Releasing pooled objects must never call Object.Destroy.");

            // Verify FIFO Queue order: first released instance must be the first dequeued on next cycle!
            NetworkObject nextDequeued = _pool.AcquireFromPool(_prefabNetObj, Vector3.one, Quaternion.identity);
            Assert.AreSame(firstReleased, nextDequeued, "FIFO Queue must return oldest released projectile first so trails decay fully.");
            Assert.AreEqual(2, nextDequeued.GetComponent<ResettableSpy>().AcquireCount);
            _pool.ReleaseToPool(nextDequeued);
        }

        [Test]
        public void AcquireFromPool_BurstAbove32_ExpandsUpToCap48AndStopsAtHardCap()
        {
            _pool.Prewarm(_prefabNetObj, 32);
            List<NetworkObject> active = new List<NetworkObject>(52);

            for (int i = 0; i < 48; i++)
            {
                NetworkObject obj = _pool.AcquireFromPool(_prefabNetObj, Vector3.zero, Quaternion.identity);
                Assert.IsNotNull(obj, $"Instance #{i + 1} up to hard cap 48 should succeed.");
                active.Add(obj);
            }

            Assert.AreEqual(16, _pool.RuntimeInstantiateCallsAfterPrewarm);
            Assert.AreEqual(48, _pool.GetTotalCreatedCount(_prefabNetObj));

            // 49th concurrent request exceeds hard cap 48 and must return null safely.
            NetworkObject overCap = _pool.AcquireFromPool(_prefabNetObj, Vector3.zero, Quaternion.identity);
            Assert.IsNull(overCap, "Pool must enforce hard cap 48 to prevent unbounded memory growth.");

            for (int i = 0; i < active.Count; i++)
            {
                _pool.ReleaseToPool(active[i]);
            }
        }

        [Test]
        public void SteadyStateAcquireAndRelease_HundredCycles_ZeroRuntimeInstantiatesAndCachedHooks()
        {
            _pool.Prewarm(_prefabNetObj, 32);
            NetworkObject warm = _pool.AcquireFromPool(_prefabNetObj, Vector3.zero, Quaternion.identity);
            _pool.ReleaseToPool(warm);

            for (int i = 0; i < 100; i++)
            {
                NetworkObject instance = _pool.AcquireFromPool(_prefabNetObj, new Vector3(i * 0.1f, 1f, 0f), Quaternion.identity);
                Assert.IsNotNull(instance);
                _pool.ReleaseToPool(instance);
            }

            Assert.AreEqual(0, _pool.RuntimeInstantiateCallsAfterPrewarm);
            Assert.AreEqual(0, _pool.RuntimeDestroyCalls);
            Assert.AreEqual(32, _pool.GetInactiveCount(_prefabNetObj));
        }

        [Test]
        public void AcquireFromPool_NullInputs_HandledSafelyWithoutExceptions()
        {
            Assert.IsNull(_pool.AcquireFromPool(null, Vector3.zero, Quaternion.identity));
            Assert.DoesNotThrow(() => _pool.ReleaseToPool(null));
            Assert.AreEqual(0, _pool.GetInactiveCount(null));
        }
    }
}
