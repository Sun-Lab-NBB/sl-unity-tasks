/// <summary>
/// Verifies the behavior of the FullScreenView class.
/// </summary>
using Gimbl;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SL.Tests.EditMode
{
    /// <summary>Verifies the behavior of the FullScreenView class.</summary>
    /// <remarks>
    /// The render path allocates a RenderTexture and blits a camera into an IMGUI repaint event, which needs a shown
    /// window and a graphics device. The registration bookkeeping that survives a domain reload and the Play Mode
    /// transition handler are therefore what this fixture covers.
    /// </remarks>
    [TestFixture]
    public class FullScreenViewTests
    {
        /// <summary>The number of registered views before the test created its own.</summary>
        private int _baselineViewCount;

        /// <summary>The view under test.</summary>
        private FullScreenView _view;

        /// <summary>Records the baseline registration count and creates the view under test.</summary>
        [SetUp]
        public void SetUp()
        {
            _baselineViewCount = FullScreenView.Views.Count;
            _view = ScriptableObject.CreateInstance<FullScreenView>();
        }

        /// <summary>Destroys the view under test when it survived the test body.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_view != null)
            {
                UnityEngine.Object.DestroyImmediate(_view);
            }
            _view = null;
        }

        /// <summary>Verifies that a newly created view registers itself in the shared view list.</summary>
        [Test]
        public void OnEnable_NewInstance_RegistersInTheSharedViewList()
        {
            Assert.AreEqual(_baselineViewCount + 1, FullScreenView.Views.Count);
            CollectionAssert.Contains(FullScreenView.Views, _view);
        }

        /// <summary>Verifies that a second enable pass does not register the same view twice.</summary>
        [Test]
        public void OnEnable_AlreadyRegisteredInstance_DoesNotRegisterTwice()
        {
            PrivateAccess.Invoke(_view, "OnEnable");

            Assert.AreEqual(_baselineViewCount + 1, FullScreenView.Views.Count);
        }

        /// <summary>Verifies that destroying a view removes it from the shared view list.</summary>
        [Test]
        public void OnDestroy_DestroyedInstance_RemovesItselfFromTheSharedViewList()
        {
            UnityEngine.Object.DestroyImmediate(_view);
            _view = null;

            Assert.AreEqual(_baselineViewCount, FullScreenView.Views.Count);
        }

        /// <summary>Verifies that a Play Mode transition other than the exit leaves the view open.</summary>
        [Test]
        public void OnPlayModeStateChanged_TransitionOtherThanExitingPlayMode_LeavesTheViewOpen()
        {
            PrivateAccess.Invoke(_view, "OnPlayModeStateChanged", PlayModeStateChange.EnteredEditMode);

            Assert.IsTrue(_view != null);
            Assert.AreEqual(_baselineViewCount + 1, FullScreenView.Views.Count);
        }
    }
}
