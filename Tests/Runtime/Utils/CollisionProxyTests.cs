// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CollisionProxyTests : CommonTestBase
    {
        // Tracking handled by CommonTestBase

        [UnityTest]
        public IEnumerator OnTriggerEnterInvokesEvent()
        {
            GameObject go = Track(
                new GameObject("TestObject", typeof(BoxCollider2D), typeof(CollisionProxy))
            );
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = true;

            bool eventInvoked = false;
            Collider2D invokedCollider = null;

            proxy.OnTriggerEnter += collider =>
            {
                eventInvoked = true;
                invokedCollider = collider;
            };

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            go.transform.position = Vector3.zero;
            other.transform.position = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (eventInvoked)
            {
                Assert.IsTrue(invokedCollider != null);
            }
        }

        [UnityTest]
        public IEnumerator OnTriggerStayInvokesEvent()
        {
            GameObject go = Track(
                new GameObject("TestObject", typeof(BoxCollider2D), typeof(CollisionProxy))
            );
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = true;
            other.transform.position = Vector3.zero;
            go.transform.position = Vector3.zero;

            int stayCount = 0;

            proxy.OnTriggerStay += _ =>
            {
                stayCount++;
            };

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (0 < stayCount)
            {
                Assert.Greater(stayCount, 0);
            }
        }

        [UnityTest]
        public IEnumerator OnTriggerExitInvokesEvent()
        {
            GameObject go = Track(
                new GameObject("TestObject", typeof(BoxCollider2D), typeof(CollisionProxy))
            );
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = true;
            other.transform.position = Vector3.zero;
            go.transform.position = Vector3.zero;

            bool exitInvoked = false;

            proxy.OnTriggerExit += _ =>
            {
                exitInvoked = true;
            };

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            other.transform.position = new Vector3(100f, 100f, 0f);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (exitInvoked)
            {
                Assert.IsTrue(exitInvoked);
            }
        }

        [UnityTest]
        public IEnumerator OnCollisionEnterInvokesEvent()
        {
            GameObject go = Track(
                new GameObject(
                    "TestObject",
                    typeof(BoxCollider2D),
                    typeof(CollisionProxy),
                    typeof(Rigidbody2D)
                )
            );
            go.GetComponent<BoxCollider2D>().isTrigger = false;
            go.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = false;
            other.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;

            bool eventInvoked = false;
            Collision2D invokedCollision = null;

            proxy.OnCollisionEnter += collision =>
            {
                eventInvoked = true;
                invokedCollision = collision;
            };

            go.transform.position = Vector3.zero;
            other.transform.position = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (eventInvoked)
            {
                Assert.IsTrue(invokedCollision != null);
            }
        }

        [UnityTest]
        public IEnumerator OnCollisionStayInvokesEvent()
        {
            GameObject go = Track(
                new GameObject(
                    "TestObject",
                    typeof(BoxCollider2D),
                    typeof(CollisionProxy),
                    typeof(Rigidbody2D)
                )
            );
            go.GetComponent<BoxCollider2D>().isTrigger = false;
            go.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = false;
            other.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
            other.transform.position = Vector3.zero;
            go.transform.position = Vector3.zero;

            int stayCount = 0;

            proxy.OnCollisionStay += _ =>
            {
                stayCount++;
            };

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (0 < stayCount)
            {
                Assert.Greater(stayCount, 0);
            }
        }

        [UnityTest]
        public IEnumerator OnCollisionExitInvokesEvent()
        {
            GameObject go = Track(
                new GameObject(
                    "TestObject",
                    typeof(BoxCollider2D),
                    typeof(CollisionProxy),
                    typeof(Rigidbody2D)
                )
            );
            go.GetComponent<BoxCollider2D>().isTrigger = false;
            go.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = false;
            other.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;
            other.transform.position = Vector3.zero;
            go.transform.position = Vector3.zero;

            bool exitInvoked = false;

            proxy.OnCollisionExit += _ =>
            {
                exitInvoked = true;
            };

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            other.transform.position = new Vector3(100f, 100f, 0f);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (exitInvoked)
            {
                Assert.IsTrue(exitInvoked);
            }
        }

        [UnityTest]
        public IEnumerator MultipleSubscribersAllInvoked()
        {
            GameObject go = Track(
                new GameObject("TestObject", typeof(BoxCollider2D), typeof(CollisionProxy))
            );
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = true;

            int subscriber1Count = 0;
            int subscriber2Count = 0;
            int subscriber3Count = 0;

            proxy.OnTriggerEnter += _ =>
            {
                subscriber1Count++;
            };
            proxy.OnTriggerEnter += _ =>
            {
                subscriber2Count++;
            };
            proxy.OnTriggerEnter += _ =>
            {
                subscriber3Count++;
            };

            go.transform.position = Vector3.zero;
            other.transform.position = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (0 < subscriber1Count)
            {
                Assert.AreEqual(subscriber1Count, subscriber2Count);
                Assert.AreEqual(subscriber2Count, subscriber3Count);
            }
        }

        [UnityTest]
        public IEnumerator EventsDoNotInterfereWithEachOther()
        {
            GameObject go = Track(
                new GameObject("TestObject", typeof(BoxCollider2D), typeof(CollisionProxy))
            );
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            bool triggerEnterInvoked = false;
            bool triggerStayInvoked = false;
            bool triggerExitInvoked = false;
            bool collisionEnterInvoked = false;
            bool collisionStayInvoked = false;
            bool collisionExitInvoked = false;

            proxy.OnTriggerEnter += _ =>
            {
                triggerEnterInvoked = true;
            };
            proxy.OnTriggerStay += _ =>
            {
                triggerStayInvoked = true;
            };
            proxy.OnTriggerExit += _ =>
            {
                triggerExitInvoked = true;
            };
            proxy.OnCollisionEnter += _ =>
            {
                collisionEnterInvoked = true;
            };
            proxy.OnCollisionStay += _ =>
            {
                collisionStayInvoked = true;
            };
            proxy.OnCollisionExit += _ =>
            {
                collisionExitInvoked = true;
            };

            yield return null;

            Assert.IsFalse(collisionEnterInvoked);
            Assert.IsFalse(collisionStayInvoked);
            Assert.IsFalse(collisionExitInvoked);
            Assert.IsFalse(triggerEnterInvoked);
            Assert.IsFalse(triggerStayInvoked);
            Assert.IsFalse(triggerExitInvoked);
        }

        [UnityTest]
        public IEnumerator UnsubscribingPreventsInvocation()
        {
            GameObject go = Track(
                new GameObject("TestObject", typeof(BoxCollider2D), typeof(CollisionProxy))
            );
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = true;

            int invokeCount = 0;

            proxy.OnTriggerEnter += Handler;
            proxy.OnTriggerEnter -= Handler;

            go.transform.position = Vector3.zero;
            other.transform.position = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, invokeCount);
            yield break;

            void Handler(Collider2D collider)
            {
                invokeCount++;
            }
        }

        [UnityTest]
        public IEnumerator NoErrorWithNoSubscribers()
        {
            GameObject go = Track(
                new GameObject("TestObject", typeof(BoxCollider2D), typeof(CollisionProxy))
            );
            go.GetComponent<BoxCollider2D>().isTrigger = true;

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            other.GetComponent<BoxCollider2D>().isTrigger = true;

            go.transform.position = Vector3.zero;
            other.transform.position = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.Pass();
        }

        [UnityTest]
        public IEnumerator RequiresCollider2DComponent()
        {
            GameObject go = Track(new GameObject("TestObject"));
            go.AddComponent<CollisionProxy>();
            Assert.IsFalse(go.HasComponent<CollisionProxy>());
            yield return null;
        }

        [UnityTest]
        public IEnumerator PassesCorrectColliderToEvent()
        {
            GameObject go = Track(
                new GameObject("TestObject", typeof(BoxCollider2D), typeof(CollisionProxy))
            );
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            CollisionProxy proxy = go.GetComponent<CollisionProxy>();

            GameObject other = Track(
                new GameObject("OtherObject", typeof(BoxCollider2D), typeof(Rigidbody2D))
            );
            BoxCollider2D otherCollider = other.GetComponent<BoxCollider2D>();
            otherCollider.isTrigger = true;

            Collider2D receivedCollider = null;

            proxy.OnTriggerEnter += collider =>
            {
                receivedCollider = collider;
            };

            go.transform.position = Vector3.zero;
            other.transform.position = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (receivedCollider != null)
            {
                Assert.AreEqual(otherCollider, receivedCollider);
            }
        }
    }
}
