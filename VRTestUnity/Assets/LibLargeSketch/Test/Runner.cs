#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;


namespace TestLibLargeSketch
{
    public class Runner : MonoBehaviour
    {
        public static string _assetPath;
        public static List<Result> _results;
        public static int _no_more_repaint;
        public static Runner _instance;

        public class Result
        {
            internal string klass;
            internal string methodname;
            internal bool? success;

            Type tp;
            IEnumerator cont;
            object instance;

            Result Copy() { return new Result { klass = klass, methodname = methodname }; }

            internal object Progress(bool call_dispose = true)
            {
                _instance.interruption = false;
                Application.logMessageReceivedThreaded += _instance.HandleLog;
                try
                {
                    if (cont == null)
                    {
                        tp = _instance.assembly.GetType("TestLibLargeSketch." + klass);
                        var ctor = tp.GetConstructor(new Type[0]);
                        instance = ctor.Invoke(new object[0]);
                        if (_instance.interruption)
                            return null;

                        var method = tp.GetMethod(methodname);
                        object result = method.Invoke(instance, new object[0]);
                        if (_instance.interruption)
                            return null;

                        if (result is IEnumerable)
                        {
                            result = ((IEnumerable)result).GetEnumerator();
                            if (_instance.interruption)
                                return null;
                        }
                        cont = (IEnumerator)result;
                    }
                    else
                    {
                        bool progress = cont.MoveNext();
                        if (_instance.interruption)
                            return null;

                        if (!progress)
                            cont = null;
                    }

                    if (cont == null)
                    {
                        if (call_dispose)
                            CallDispose();
                        if (_instance.interruption)
                            return null;

                        success = true;
                        return null;
                    }
                    else
                    {
                        object x = cont.Current;
                        if (x == null)
                            x = 0;
                        return x;
                    }
                }
                finally
                {
                    Application.logMessageReceivedThreaded -= _instance.HandleLog;
                }
            }

            void CallDispose()
            {
                var dispose_method = tp.GetMethod("Dispose");
                if (dispose_method != null)
                    dispose_method.Invoke(instance, new object[0]);
            }

            internal IEnumerator RunTestAgain(bool really_run)
            {
                if (_instance.broken != null)
                    _instance.broken.CallDispose();

                if (really_run)
                {
                    _instance.broken = Copy();
                    object x;
                    while ((x = _instance.broken.Progress(call_dispose: false)) != null)
                        yield return x;
                }
                else
                    _instance.broken = null;
            }

            internal bool SameAs(Result other)
            {
                return (other != null && other.klass == klass && other.methodname == methodname);
            }
        }


        bool interruption;
        Assembly assembly;
        internal Result broken;
        internal int successes;

        IEnumerator Start()
        {
            var mono_script = MonoScript.FromMonoBehaviour(this);
            _assetPath = AssetDatabase.GetAssetPath(mono_script);
            _assetPath = System.IO.Path.GetDirectoryName(_assetPath);

            try
            {
                int successes = 0;
                _instance = this;
                _results = new List<Result>();
                assembly = Assembly.GetExecutingAssembly();
                foreach (var tp in assembly.GetTypes())
                {
                    if (tp.Namespace == "TestLibLargeSketch" && tp.Name.StartsWith("Test") && !tp.IsAbstract)
                    {
                        int class_passed = 0;

                        foreach (var method in tp.GetMethods())
                        {
                            if (method.Name.StartsWith("Test"))
                            {
                                broken = new Result { klass = tp.Name, methodname = method.Name };
                                _results.Add(broken);

                                object x;
                                while ((x = broken.Progress()) != null)
                                    yield return x;

                                if (broken.success != true)
                                {
                                    /* stop at the first failure, keeping the non-Disposed state */
                                    broken.success = false;
                                    yield break;
                                }
                                broken = null;
                                class_passed++;

                                yield return 0;
                            }
                        }

                        Debug.Log(tp.Name + ": " + class_passed + " tests passed");
                        successes += class_passed;

                        yield return 0;
                    }
                }
                Debug.Log("All " + successes + " tests passed.");
                this.successes = successes;
            }
            finally
            {
                _no_more_repaint = 1;
            }
        }

        void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (type != LogType.Log)
                interruption = true;
        }
    }


    public abstract class TestClass
    {
        public virtual void Dispose() { }

        public UnityEngine.Object LoadObject(string file_name)
        {
            return AssetDatabase.LoadMainAssetAtPath(Runner._assetPath + "/" + file_name);
        }

        public GameObject Instantiate(string file_name)
        {
            var go = UnityEngine.Object.Instantiate((GameObject)LoadObject(file_name));
            go.name = file_name;
            return go;
        }
    }


    [CustomEditor(typeof(Runner))]
    public class RunnerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (!Application.isPlaying)
            {
                base.OnInspectorGUI();
                GUILayout.Label("");
                GUILayout.Label("Enter Play mode to run the tests.");
                return;
            }

            if (Runner._results == null)
                return;

            GUILayout.BeginVertical("Box");

            foreach (var result in Runner._results)
            {
                string text = result.klass + "." + result.methodname;
                if (result.success == false)
                    text += "  FAILED";

                bool selected = result.SameAs(Runner._instance.broken);

                if (GUILayout.Toggle(selected, text) != selected)
                {
                    Runner._instance.StartCoroutine(result.RunTestAgain(!selected));
                }
            }
            if (Runner._instance.successes > 0)
                GUILayout.Label("All " + Runner._instance.successes + " tests passed.");
            GUILayout.EndVertical();
        }

        public override bool RequiresConstantRepaint()
        {
            if (!Application.isPlaying)
                return false;

            bool result = (Runner._no_more_repaint <= 1);
            if (Runner._no_more_repaint == 1)
                Runner._no_more_repaint = 2;
            return result;
        }
    }
}
#endif
