
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace UdonSharp.Tests
{
    [AddComponentMenu("Udon Sharp/Tests/LocalFunctionTest")]
    public class LocalFunctionTest : UdonSharpBehaviour
    {
        [System.NonSerialized]
        public IntegrationTestSuite tester;

        int eventCounter;
        LocalFunctionTest selfReference;

        public void ExecuteTests()
        {
            selfReference = this;
            TestCustomEvents();
            TestFunctionParameters();
            TestIntermediateReturn();
            TestObjectArrayArg();
            TestStringCopy();
        }

        int GetCountAmount() => 4;

        public void IncrementCounter()
        {
            eventCounter += GetCountAmount();
        }

        void TestCustomEvents()
        {
            eventCounter = 0;

            SendCustomEvent("IncrementCounter");
            SendCustomEvent("IncrementCounter");
            SendCustomEvent("IncrementCounter");

            tester.TestAssertion("Custom Event Calls", eventCounter == 12);

            selfReference.SendCustomEvent("IncrementCounter");
            tester.TestAssertion("Reference Custom Event Calls", eventCounter == 16);
        }

        int AddIntegers(int a, int b)
        {
            ++a;
            return a + b;
        }

        void TestFunctionParameters()
        {
            int a = 4;
            int addResult = AddIntegers(a, 6);
            tester.TestAssertion("Function parameters", addResult == 11 && a == 4);
        }

        int AddIntegers2(int a, int b)
        {
            return a + b;
        }

        void TestIntermediateReturn()
        {
            int result = AddIntegers2(2, 4) + AddIntegers2(6, 9);
            tester.TestAssertion("Method Intermediate Return Value", result == 21);
        }

        private object[] AddFirstToObjectArray(object[] a, object b)
        {
            var n = new object[a.Length + 1];
            for (var i = 0; i != a.Length; i++)
            {
                n[i + 1] = a[i];
            }
            n[0] = b;
            return n;
        }

        void TestObjectArrayArg()
        {
            object[] work = new object[4];
            object tempData = null;
            object level = this;

            work = AddFirstToObjectArray(work, new object[] { tempData, level });

            object[] insertedVal = (object[])work[0];

#pragma warning disable CS0252 // Possible unintended reference comparison; left hand side needs cast
            tester.TestAssertion("Object array to object conversion on creation", insertedVal[1] == this);
#pragma warning restore CS0252 // Possible unintended reference comparison; left hand side needs cast
        }

        string targetVal = "";
        void SetStr(string val)
        {
            targetVal = val;
        }

        // https://github.com/Merlin-san/UdonSharp/issues/40
        // todo: should probably be moved to another more applicable test suite, probably a string interpolation suite
        void TestStringCopy()
        {
            string testStr = $"test {1}";
            testStr = $"";

            tester.TestAssertion("Basic string interpolation clear", testStr == "");

            testStr = $"no interpolation here";

            tester.TestAssertion("Basic string interpolation set with no interpolation", testStr == "no interpolation here");

            targetVal = "";

            SetStr($"{20}");

            tester.TestAssertion("Set string interpolation argument", targetVal == "20");

            SetStr($"");

            tester.TestAssertion("String interpolation arg clear", targetVal == "");

            SetStr("Hello");

            tester.TestAssertion("Basic string arg set", targetVal == "Hello");

            SetStr("");

            tester.TestAssertion("Basic string arg clear", targetVal == "");
        }
    }
}
