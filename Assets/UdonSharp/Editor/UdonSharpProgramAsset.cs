﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor.ProgramSources;
using VRC.Udon.Editor.ProgramSources.Attributes;
using VRC.Udon.EditorBindings;
using VRC.Udon.Serialization.OdinSerializer;

[assembly: UdonProgramSourceNewMenu(typeof(UdonSharp.UdonSharpProgramAsset), "Udon C# Program Asset")]

namespace UdonSharp
{
    [CreateAssetMenu(menuName = "VRChat/Udon/Udon C# Program Asset", fileName = "New Udon C# Program Asset")]
    public class UdonSharpProgramAsset : UdonAssemblyProgramAsset
    {
        [SerializeField]
        public MonoScript sourceCsScript;

        [NonSerialized, OdinSerialize]
        public Dictionary<string, FieldDefinition> fieldDefinitions;

        [HideInInspector]
        public string behaviourIDHeapVarName;

        [HideInInspector]
        public List<string> compileErrors = new List<string>();

        [HideInInspector]
        public ClassDebugInfo debugInfo = null;

        [SerializeField]
        private bool hasInteractEvent = false;

        [SerializeField, HideInInspector]
        private SerializationData serializationData;

        private bool showProgramUasm = false;
        private bool showExtraOptions = false;
        private string customEventName = "";

        private UdonBehaviour currentBehaviour = null;

        private static GUIStyle errorTextStyle;
        private static GUIStyle undoLabelStyle;
        private static GUIContent undoArrowLight;
        private static GUIContent undoArrowDark;
        private static GUIContent undoArrowContent;
        private static Texture2D clearColorLight;
        private static Texture2D clearColorDark;
        private static GUIStyle clearColorStyle;

        private void DrawCompileErrorTextArea()
        {
            if (compileErrors == null || compileErrors.Count == 0)
                return;

            if (errorTextStyle == null)
            {
                errorTextStyle = new GUIStyle(EditorStyles.textArea);
                errorTextStyle.normal.textColor = new Color32(211, 34, 34, 255);
                errorTextStyle.focused.textColor = errorTextStyle.normal.textColor;
            }

            // todo: convert this to a tree view that just has a list of selectable items that jump to the error
            EditorGUILayout.LabelField($"Compile Error{(compileErrors.Count > 1 ? "s" : "")}", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(string.Join("\n", compileErrors.Select(e => e.Replace("[<color=#FF00FF>UdonSharp</color>] ", ""))), errorTextStyle);
        }

        protected override void DrawProgramSourceGUI(UdonBehaviour udonBehaviour, ref bool dirty)
        {
            if (undoLabelStyle == null || 
                undoArrowDark == null || 
                undoArrowLight == null ||
                clearColorDark == null ||
                clearColorLight == null ||
                clearColorStyle == null)
            {
                undoLabelStyle = new GUIStyle(EditorStyles.label);
                undoLabelStyle.alignment = TextAnchor.MiddleCenter;
                undoLabelStyle.padding = new RectOffset(0, 0, 1, 0);
                undoLabelStyle.margin = new RectOffset(0, 0, 0, 0);
                undoLabelStyle.border = new RectOffset(0, 0, 0, 0);
                undoLabelStyle.stretchWidth = false;
                undoLabelStyle.stretchHeight = false;

                undoArrowLight = new GUIContent((Texture)EditorGUIUtility.Load("Assets/UdonSharp/Editor/Resources/UndoArrowLight.png"), "Reset to default value");
                undoArrowDark = new GUIContent((Texture)EditorGUIUtility.Load("Assets/UdonSharp/Editor/Resources/UndoArrowBlack.png"), "Reset to default value");

                Texture2D clearColorLightTex = new Texture2D(1, 1);
                clearColorLightTex.SetPixel(0, 0, new Color32(194, 194, 194, 255));
                clearColorLightTex.Apply();

                clearColorLight = clearColorLightTex;

                Texture2D clearColorDarkTex = new Texture2D(1, 1);
                clearColorDarkTex.SetPixel(0, 0, new Color32(56, 56, 56, 255));
                clearColorDarkTex.Apply();

                clearColorDark = clearColorDarkTex;

                clearColorStyle = new GUIStyle();
            }
            
            undoArrowContent = EditorGUIUtility.isProSkin ? undoArrowLight : undoArrowDark;
            clearColorStyle.normal.background = EditorGUIUtility.isProSkin ? clearColorDark : clearColorLight;

            currentBehaviour = udonBehaviour;

            EditorGUI.BeginDisabledGroup(udonBehaviour);
            if (udonBehaviour)
                EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            MonoScript newSourceCsScript = (MonoScript)EditorGUILayout.ObjectField("Source Script", sourceCsScript, typeof(MonoScript), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Changed source C# script");
                sourceCsScript = newSourceCsScript;
                dirty = true;
            }
            if (udonBehaviour)
                EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();

            if (sourceCsScript == null)
            {
                if (DrawCreateScriptButton())
                {
                    dirty = true;
                }
                return;
            }
            
            object behaviourID = null;
            bool shouldUseRuntimeValue = EditorApplication.isPlaying && currentBehaviour != null;

            // UdonBehaviours won't have valid heap values unless they have been enabled once to run their initialization. 
            // So we check against a value we know will exist to make sure we can use the heap variables.
            if (shouldUseRuntimeValue)
            {
                behaviourID = currentBehaviour.GetProgramVariable(behaviourIDHeapVarName);
                if (behaviourID == null)
                    shouldUseRuntimeValue = false;
            }

            // Just manually break the disabled scope in the UdonBehaviourEditor default drawing for now
            GUI.enabled = GUI.enabled || shouldUseRuntimeValue;
            shouldUseRuntimeValue &= GUI.enabled;

            if (currentBehaviour != null && hasInteractEvent)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Interact", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                string newInteractText = EditorGUILayout.TextField("Interaction Text", currentBehaviour.interactText);
                float newProximity = EditorGUILayout.Slider("Proximity", currentBehaviour.proximity, 0f, 100f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(currentBehaviour, "Change interact property");

                    currentBehaviour.interactText = newInteractText;
                    currentBehaviour.proximity = newProximity;
                }

                EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);
                if (GUILayout.Button("Trigger Interact"))
                    currentBehaviour.SendCustomEvent("_interact");
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space();

            DrawPublicVariables(udonBehaviour, ref dirty);

            if (currentBehaviour != null && !shouldUseRuntimeValue && program != null)
            {
                string[] exportedSymbolNames = program.SymbolTable.GetExportedSymbols();

                foreach (string exportedSymbolName in exportedSymbolNames)
                {
                    bool foundValue = currentBehaviour.publicVariables.TryGetVariableValue(exportedSymbolName, out var variableValue);
                    bool foundType = currentBehaviour.publicVariables.TryGetVariableType(exportedSymbolName, out var variableType);

                    // Remove this variable from the publicVariable list since UdonBehaviours set all null GameObjects, UdonBehaviours, and Transforms to the current behavior's equivalent object regardless of if it's marked as a `null` heap variable or `this`
                    // This default behavior is not the same as Unity, where the references are just left null. And more importantly, it assumes that the user has interacted with the inspector on that object at some point which cannot be guaranteed. 
                    // Specifically, if the user adds some public variable to a class, and multiple objects in the scene reference the program asset, 
                    //   the user will need to go through each of the objects' inspectors to make sure each UdonBehavior has its `publicVariables` variable populated by the inspector
                    if (foundValue && foundType &&
                        variableValue == null &&
                        (variableType == typeof(GameObject) || variableType == typeof(UdonBehaviour) || variableType == typeof(Transform)))
                    {
                        currentBehaviour.publicVariables.RemoveVariable(exportedSymbolName);
                    }
                }
            }

            DrawCompileErrorTextArea();
            DrawAssemblyErrorTextArea();

            EditorGUILayout.Space();

            if (GUILayout.Button("Force Compile Script"))
            {
                CompileCsProgram();
            }

            if (GUILayout.Button("Compile All UdonSharp Programs"))
            {
                CompileAllCsPrograms();
            }

            EditorGUILayout.Space();
            
            showExtraOptions = EditorGUILayout.Foldout(showExtraOptions, "Utilities");
            if (showExtraOptions)
            {
                EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);

                if (GUILayout.Button("Send Custom Event"))
                {
                    if (currentBehaviour != null)
                        currentBehaviour.SendCustomEvent(customEventName);
                }

                customEventName = EditorGUILayout.TextField("Event Name:", customEventName);

                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(EditorApplication.isPlaying);

                EditorGUILayout.Space();

                if (GUILayout.Button("Export to Assembly Asset"))
                {
                    string savePath = EditorUtility.SaveFilePanelInProject("Assembly asset save location", Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(sourceCsScript)), "asset", "Choose a save location for the assembly asset");

                    if (savePath.Length > 0)
                    {
                        UdonSharpEditorUtility.UdonSharpProgramToAssemblyProgram(this, savePath);
                    }
                }
                EditorGUI.EndDisabledGroup();
            }

            showProgramUasm = EditorGUILayout.Foldout(showProgramUasm, "Compiled C# Assembly");
            if (showProgramUasm)
            {
                DrawAssemblyTextArea(/*!Application.isPlaying*/ false, ref dirty);

                if (program != null)
                    DrawProgramDisassembly();
            }

            currentBehaviour = null;
        }

        protected override void RefreshProgramImpl()
        {
            bool hasAssemblyError = typeof(UdonAssemblyProgramAsset).GetField("assemblyError", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this) != null;

            if (sourceCsScript != null &&
                !EditorApplication.isCompiling &&
                !EditorApplication.isUpdating &&
                !hasAssemblyError &&
                compileErrors.Count == 0)
            {
                CompileCsProgram();
            }
        }
        
        protected override object GetPublicVariableDefaultValue(string symbol, Type type)
        {
            return program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(symbol));
        }

        public void CompileCsProgram()
        {
            try
            {
                UdonSharpCompiler compiler = new UdonSharpCompiler(this);
                compiler.Compile();
            }
            catch (Exception e)
            {
                compileErrors.Add(e.ToString());
                throw e;
            }
        }

        public static void CompileAllCsPrograms()
        {
            string[] udonSharpDataAssets = AssetDatabase.FindAssets($"t:{typeof(UdonSharpProgramAsset).Name}");

            List<UdonSharpProgramAsset> udonSharpPrograms = new List<UdonSharpProgramAsset>();

            foreach (string dataGuid in udonSharpDataAssets)
            {
                udonSharpPrograms.Add(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(dataGuid)));
            }

            UdonSharpCompiler compiler = new UdonSharpCompiler(udonSharpPrograms.ToArray());
            compiler.Compile();
        }

        static UdonEditorInterface editorInterfaceInstance;
        static UdonSharp.HeapFactory heapFactoryInstance;

        public void AssembleCsProgram(uint heapSize)
        {
            if (editorInterfaceInstance == null || heapFactoryInstance == null)
            {
                // The heap size is determined by the symbol count + the unique extern string count
                heapFactoryInstance = new UdonSharp.HeapFactory();
                editorInterfaceInstance = new UdonEditorInterface(null, heapFactoryInstance, null, null, null, null, null, null, null);
                editorInterfaceInstance.AddTypeResolver(new UdonBehaviourTypeResolver()); // todo: can be removed with SDK's >= VRCSDK-UDON-2020.06.15.14.08_Public
            }

            heapFactoryInstance.FactoryHeapSize = heapSize;

            FieldInfo assemblyError = typeof(UdonAssemblyProgramAsset).GetField("assemblyError", BindingFlags.NonPublic | BindingFlags.Instance);

            try
            {
                program = editorInterfaceInstance.Assemble(udonAssembly);
                assemblyError.SetValue(this, null);

                hasInteractEvent = false;

                foreach (string entryPoint in program.EntryPoints.GetExportedSymbols())
                {
                    if (entryPoint == "_interact")
                    {
                        hasInteractEvent = true;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                program = null;
                assemblyError.SetValue(this, e.Message);
                Debug.LogException(e);
            }
        }

        public void ApplyProgram()
        {
            SerializedProgramAsset.StoreProgram(program);
            EditorUtility.SetDirty(this);
        }

        public void SetUdonAssembly(string assembly)
        {
            udonAssembly = assembly;
        }
        
        public IUdonProgram GetRealProgram()
        {
            return program;
        }

        // Skips the property since it will create an asset if one doesn't exist and we do not want that.
        public AbstractSerializedUdonProgramAsset GetSerializedUdonProgramAsset()
        {
            return serializedUdonProgramAsset;
        }

        private bool DrawCreateScriptButton()
        {
            if (GUILayout.Button("Create Script"))
            {
                string thisPath = AssetDatabase.GetAssetPath(this);
                //string initialPath = Path.GetDirectoryName(thisPath);
                string fileName = Path.GetFileNameWithoutExtension(thisPath).Replace(" Udon C# Program Asset", "").Replace(" ", "").Replace("#", "Sharp");

                string chosenFilePath = EditorUtility.SaveFilePanelInProject("Save UdonSharp File", fileName, "cs", "Save UdonSharp file");

                if (chosenFilePath.Length > 0)
                {
                    string fileContents = UdonSharpSettings.GetProgramTemplateString(Path.GetFileNameWithoutExtension(chosenFilePath));

                    File.WriteAllText(chosenFilePath, fileContents);

                    AssetDatabase.ImportAsset(chosenFilePath, ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.Refresh();

                    sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(chosenFilePath);

                    return true;
                }
            }

            return false;
        }

        private static MonoScript currentUserScript = null;
        private UnityEngine.Object ValidateObjectReference(UnityEngine.Object[] references, System.Type objType, SerializedProperty property, Enum options = null)
        {
            if (property != null)
                throw new ArgumentException("Serialized property on validate object reference should be null!");

            if (currentUserScript != null || 
                objType == typeof(UdonBehaviour) || 
                objType == typeof(UdonSharpBehaviour))
            {
                foreach (UnityEngine.Object reference in references)
                {
                    GameObject referenceObject = reference as GameObject;
                    UdonBehaviour referenceBehaviour = reference as UdonBehaviour;

                    if (referenceObject != null)
                    {
                        UdonBehaviour[] components = referenceObject.GetComponents<UdonBehaviour>();

                        UdonBehaviour foundComponent = null;

                        foreach (UdonBehaviour component in components)
                        {
                            foundComponent = ValidateObjectReference(new UnityEngine.Object[] { component }, objType, null, UdonSyncMode.NotSynced /* just any enum, we don't care */) as UdonBehaviour;

                            if (foundComponent != null)
                            {
                                return foundComponent;
                            }
                        }
                    }
                    else if (referenceBehaviour != null)
                    {
                        if (referenceBehaviour.programSource != null &&
                            referenceBehaviour.programSource is UdonSharpProgramAsset udonSharpProgram &&
                            udonSharpProgram.sourceCsScript != null)
                        {
                            if (currentUserScript == null || // If this is null, the field is referencing a generic UdonBehaviour or UdonSharpBehaviour instead of a behaviour of a certain type that inherits from UdonSharpBehaviour.
                                udonSharpProgram.sourceCsScript == currentUserScript)
                                return referenceBehaviour;
                        }
                    }
                }
            }
            else
            {
                // Fallback to default handling if the user has not compiled with the new info
                if (references[0] != null && references[0] is GameObject && typeof(Component).IsAssignableFrom(objType))
                {
                    GameObject gameObject = (GameObject)references[0];
                    references = gameObject.GetComponents(typeof(Component));
                }
                foreach (UnityEngine.Object component in references)
                {
                    if (component != null && objType.IsAssignableFrom(component.GetType()))
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        private bool IsNormalUnityObject(System.Type declaredType, FieldDefinition fieldDefinition)
        {
            return !UdonSharpUtils.IsUserDefinedBehaviour(declaredType) && (fieldDefinition == null || fieldDefinition.fieldSymbol.userCsType == null || !fieldDefinition.fieldSymbol.IsUserDefinedBehaviour());
        }

        private object DrawUnityObjectField(GUIContent fieldName, string symbol, (object value, Type declaredType, FieldDefinition symbolField) publicVariable, ref bool dirty)
        {
            (object value, Type declaredType, FieldDefinition symbolField) = publicVariable;

            FieldDefinition fieldDefinition = symbolField;
            
            if (IsNormalUnityObject(declaredType, fieldDefinition))
                return EditorGUILayout.ObjectField(fieldName, (UnityEngine.Object)value, declaredType, true);

            MethodInfo doObjectFieldMethod = typeof(EditorGUI).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(e => e.Name == "DoObjectField" && e.GetParameters().Length == 8).FirstOrDefault();

            if (doObjectFieldMethod == null)
                throw new Exception("Could not find DoObjectField() method");

            Rect objectRect = EditorGUILayout.GetControlRect();
            Rect originalRect = objectRect;
            int id = GUIUtility.GetControlID(typeof(UnityEngine.Object).GetHashCode(), FocusType.Keyboard, originalRect);

            System.Type validatorDelegateType = typeof(EditorGUI).GetNestedType("ObjectFieldValidator", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo validateMethodInfo = typeof(UdonSharpProgramAsset).GetMethod("ValidateObjectReference", BindingFlags.NonPublic | BindingFlags.Instance);

            objectRect = EditorGUI.PrefixLabel(objectRect, id, new GUIContent(fieldName));

            currentUserScript = fieldDefinition.userBehaviourSource;
            
            UnityEngine.Object objectFieldValue = (UnityEngine.Object)doObjectFieldMethod.Invoke(null, new object[] {
                objectRect,
                objectRect,
                id,
                (UnityEngine.Object)value,
                typeof(UdonBehaviour),
                null,
                Delegate.CreateDelegate(validatorDelegateType, this, validateMethodInfo),
                true
            });

            currentUserScript = null;

            string labelText;
            System.Type variableType = fieldDefinition.fieldSymbol.userCsType;

            while (variableType.IsArray)
                variableType = variableType.GetElementType();

            if (objectFieldValue == null)
            {
                labelText = $"None ({ObjectNames.NicifyVariableName(variableType.Name)})";
            }
            else
            {
                UdonBehaviour targetBehaviour = objectFieldValue as UdonBehaviour;
                UdonSharpProgramAsset targetProgramAsset = targetBehaviour?.programSource as UdonSharpProgramAsset;
                if (targetProgramAsset && targetProgramAsset.sourceCsScript)
                    variableType = targetProgramAsset.sourceCsScript.GetClass();

                labelText = $"{objectFieldValue.name} ({variableType.Name})";
            }
            
            // Overwrite any content already on the background from drawing the normal object field
            GUI.Box(originalRect, GUIContent.none, clearColorStyle);

            // Manually draw this using the same ID so that we can get some of the style information to bleed over
            objectRect = EditorGUI.PrefixLabel(originalRect, id, new GUIContent(fieldName));
            if (Event.current.type == EventType.Repaint)
                EditorStyles.objectField.Draw(objectRect, new GUIContent(labelText, objectFieldValue == null ? null : AssetPreview.GetMiniThumbnail(this)), id);

            return objectFieldValue;
        }

        [NonSerialized]
        private Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();

        private object DrawFieldForType(string fieldName, string symbol, (object value, Type declaredType, FieldDefinition symbolField) publicVariable, System.Type currentType, ref bool dirty, bool enabled)
        {
            bool isArrayElement = fieldName != null;

            (object value, Type declaredType, FieldDefinition symbolField) = publicVariable;

            FieldDefinition fieldDefinition = symbolField;
            
            if (fieldName == null)
                fieldName = ObjectNames.NicifyVariableName(symbol);

            GUIContent fieldLabel = null;

            TooltipAttribute tooltip = fieldDefinition == null ? null : fieldDefinition.GetAttribute<TooltipAttribute>();

            RangeAttribute range = fieldDefinition == null ? null : fieldDefinition.GetAttribute<RangeAttribute>();

            if (tooltip != null)
                fieldLabel = new GUIContent(fieldName, tooltip.tooltip);
            else
                fieldLabel = new GUIContent(fieldName);
            
            if (declaredType.IsArray)
            {
                bool foldoutEnabled;

                if (!foldoutStates.TryGetValue(symbol, out foldoutEnabled))
                {
                    foldoutStates.Add(symbol, false);
                }

                Event tempEvent = new Event(Event.current);

                Rect foldoutRect = EditorGUILayout.GetControlRect();
                foldoutEnabled = EditorGUI.Foldout(foldoutRect, foldoutEnabled, fieldLabel, true);

                foldoutStates[symbol] = foldoutEnabled;

                Type arrayDataType = currentType;

                bool canCopyPlace = true;

                if (UdonSharpUtils.IsUserJaggedArray(currentType))
                {
                    canCopyPlace = false;
                    arrayDataType = typeof(object[]);
                }
                else if (currentType.IsArray && UdonSharpUtils.IsUserDefinedBehaviour(currentType))
                {
                    arrayDataType = typeof(Component[]);
                }

                switch (tempEvent.type)
                {
                    case EventType.DragExited:
                        if (GUI.enabled)
                            HandleUtility.Repaint();
                        break;
                    case EventType.DragUpdated:
                    case EventType.DragPerform:
                        if (foldoutRect.Contains(tempEvent.mousePosition) && GUI.enabled && canCopyPlace)
                        {
                            int foldoutId = (int)typeof(EditorGUIUtility).GetField("s_LastControlID", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

                            UnityEngine.Object[] references = DragAndDrop.objectReferences;
                            UnityEngine.Object[] objArray = new UnityEngine.Object[1];

                            bool acceptedDrag = false;

                            List<UnityEngine.Object> draggedReferences = new List<UnityEngine.Object>();

                            currentUserScript = fieldDefinition?.userBehaviourSource;
                            foreach (UnityEngine.Object obj in references)
                            {
                                objArray[0] = obj;
                                UnityEngine.Object validatedObject = ValidateObjectReference(objArray, currentType.GetElementType(), null);

                                if (validatedObject != null)
                                {
                                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                                    if (tempEvent.type == EventType.DragPerform)
                                    {
                                        draggedReferences.Add(validatedObject);
                                        acceptedDrag = true;
                                        DragAndDrop.activeControlID = 0;
                                    }
                                    else
                                    {
                                        DragAndDrop.activeControlID = foldoutId;
                                    }
                                }
                            }
                            currentUserScript = null;

                            if (acceptedDrag)
                            {
                                Array oldArray = (Array)value;

                                Array newArray = Activator.CreateInstance(arrayDataType, new object[] { oldArray.Length + draggedReferences.Count }) as Array;
                                Array.Copy(oldArray, newArray, oldArray.Length);
                                Array.Copy(draggedReferences.ToArray(), 0, newArray, oldArray.Length, draggedReferences.Count);

                                GUI.changed = true;
                                DragAndDrop.AcceptDrag();

                                return newArray;
                            }
                        }

                        break;
                }

                if (foldoutEnabled)
                {
                    Type elementType = currentType.GetElementType();

                    if (value == null)
                    {
                        GUI.changed = true;
                        return Activator.CreateInstance(arrayDataType, new object[] { 0 });
                    }

                    EditorGUI.indentLevel++;

                    Array valueArray = value as Array;

                    using (EditorGUILayout.VerticalScope verticalScope = new EditorGUILayout.VerticalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        int newLength = EditorGUILayout.DelayedIntField("Size", valueArray.Length);
                        if (newLength < 0)
                        {
                            Debug.LogError("Array size must be non-negative.");
                            newLength = valueArray.Length;
                        }

                        // We need to resize the array
                        if (EditorGUI.EndChangeCheck())
                        {
                            Array newArray = Activator.CreateInstance(arrayDataType, new object[] { newLength }) as Array;

                            for (int i = 0; i < newLength && i < valueArray.Length; ++i)
                            {
                                newArray.SetValue(valueArray.GetValue(i), i);
                            }

                            // Fill the empty elements with the last element's value when expanding the array
                            if (valueArray.Length > 0 && newLength > valueArray.Length)
                            {
                                object lastElementVal = valueArray.GetValue(valueArray.Length - 1);
                                if (!(lastElementVal is Array)) // We do not want copies of the reference to a jagged array element to be copied
                                {
                                    for (int i = valueArray.Length; i < newLength; ++i)
                                    {
                                        newArray.SetValue(lastElementVal, i);
                                    }
                                }
                            }

                            EditorGUI.indentLevel--;
                            return newArray;
                        }

                        for (int i = 0; i < valueArray.Length; ++i)
                        {
                            var elementData = (valueArray.GetValue(i), elementType, fieldDefinition);

                            EditorGUI.BeginChangeCheck();
                            object newArrayVal = DrawFieldForType($"Element {i}", $"{symbol}_element{i}", elementData, currentType.GetElementType(), ref dirty, enabled);

                            if (EditorGUI.EndChangeCheck())
                            {
                                valueArray = (Array)valueArray.Clone();
                                valueArray.SetValue(newArrayVal, i);
                            }
                        }

                        EditorGUI.indentLevel--;

                        return valueArray;
                    }
                }
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(declaredType))
            {
                return DrawUnityObjectField(fieldLabel, symbol, (value, declaredType, symbolField), ref dirty);
            }
            else if (declaredType == typeof(string))
            {
                TextAreaAttribute textArea = fieldDefinition == null ? null : fieldDefinition.GetAttribute<TextAreaAttribute>();

                if (textArea != null)
                {
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(fieldLabel);
                    string textAreaText = EditorGUILayout.TextArea((string)value);
                    EditorGUILayout.EndVertical();

                    return textAreaText;
                }
                else
                {
                    return EditorGUILayout.TextField(fieldLabel, (string)value);
                }
            }
            else if (declaredType == typeof(float))
            {
                if(range != null)
                    return EditorGUILayout.Slider(fieldLabel, (float?)value ?? default, range.min, range.max);
                else
                    return EditorGUILayout.FloatField(fieldLabel, (float?)value ?? default);
            }
            else if (declaredType == typeof(double))
            {
                if(range != null)
                    return EditorGUILayout.Slider(fieldLabel, (float)((double?)value ?? default), range.min, range.max);
                else
                    return EditorGUILayout.DoubleField(fieldLabel, (double?)value ?? default);
            }
            else if (declaredType == typeof(int))
            {
                if(range != null)
                    return EditorGUILayout.IntSlider(fieldLabel, (int?)value ?? default, (int)range.min, (int)range.max);
                else
                    return EditorGUILayout.IntField(fieldLabel, (int?)value ?? default);
            }
            else if (declaredType == typeof(long))
            {
                return EditorGUILayout.LongField(fieldLabel, (long?)value ?? default);
            }
            else if (declaredType == typeof(bool))
            {
                return EditorGUILayout.Toggle(fieldLabel, (bool?)value ?? default);
            }
            else if (declaredType == typeof(Vector2))
            {
                return EditorGUILayout.Vector2Field(fieldLabel, (Vector2?)value ?? default);
            }
            else if (declaredType == typeof(Vector3))
            {
                return EditorGUILayout.Vector3Field(fieldLabel, (Vector3?)value ?? default);
            }
            else if (declaredType == typeof(Vector4))
            {
                return EditorGUILayout.Vector4Field(fieldLabel, (Vector4?)value ?? default);
            }
            else if (declaredType == typeof(Color))
            {
                ColorUsageAttribute colorUsage = fieldDefinition == null ? null : fieldDefinition.GetAttribute<ColorUsageAttribute>();

                if (colorUsage != null)
                {
                    return EditorGUILayout.ColorField(fieldLabel, (Color?)value ?? default, false, colorUsage.showAlpha, colorUsage.hdr);
                }
                else
                {
                    return EditorGUILayout.ColorField(fieldLabel, (Color?)value ?? default);
                }
            }
            else if (declaredType == typeof(Color32))
            {
                return (Color32)EditorGUILayout.ColorField(fieldLabel, (Color32?)value ?? default);
            }
            else if (declaredType == typeof(Quaternion))
            {
                Quaternion quatVal = (Quaternion?)value ?? default;
                Vector4 newQuat = EditorGUILayout.Vector4Field(fieldLabel, new Vector4(quatVal.x, quatVal.y, quatVal.z, quatVal.w));
                return new Quaternion(newQuat.x, newQuat.y, newQuat.z, newQuat.w);
            }
            else if (declaredType == typeof(Bounds))
            {
                return EditorGUILayout.BoundsField(fieldLabel, (Bounds?)value ?? default);
            }
            else if (declaredType == typeof(ParticleSystem.MinMaxCurve))
            {
                // This is just matching the standard Udon editor's capability at the moment, I want to eventually switch it to use the proper curve editor, but that will take a chunk of work
                ParticleSystem.MinMaxCurve minMaxCurve = (ParticleSystem.MinMaxCurve?)value ?? default;

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(fieldLabel);
                EditorGUI.indentLevel++;
                minMaxCurve.curveMultiplier = EditorGUILayout.FloatField("Multiplier", minMaxCurve.curveMultiplier);
                minMaxCurve.curveMin = EditorGUILayout.CurveField("Min Curve", minMaxCurve.curveMin);
                minMaxCurve.curveMax = EditorGUILayout.CurveField("Max Curve", minMaxCurve.curveMax);

                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();

                return minMaxCurve;
            }
            else if (declaredType == typeof(LayerMask)) // Lazy layermask support, todo: make it more like the editor layer mask and also don't do all these LINQ operations and such every draw
            {
                return (LayerMask)EditorGUILayout.MaskField(fieldLabel, (LayerMask?)value ?? default, Enumerable.Range(0, 32).Select(e => LayerMask.LayerToName(e).Length > 0 ? e + ": " + LayerMask.LayerToName(e) : "").ToArray());
            }
            else if (declaredType.IsEnum)
            {
                return EditorGUILayout.EnumPopup(fieldLabel, (Enum)(value ?? Activator.CreateInstance(declaredType)));
            }
            else if (declaredType == typeof(System.Type))
            {
                string typeName = value != null ? ((Type)value).FullName : "null";
                EditorGUILayout.LabelField(fieldLabel, typeName);
            }
            else if (declaredType == typeof(Gradient))
            {
                GradientUsageAttribute gradientUsage = fieldDefinition == null ? null : fieldDefinition.GetAttribute<GradientUsageAttribute>();

                if (value == null)
                {
                    value = new Gradient();
                    GUI.changed = true;
                }

                if (gradientUsage != null)
                {
                    return EditorGUILayout.GradientField(fieldLabel, (Gradient)value, gradientUsage.hdr);
                }
                else
                {
                    return EditorGUILayout.GradientField(fieldLabel, (Gradient)value);
                }
            }
            else if (declaredType == typeof(AnimationCurve))
            {
                return EditorGUILayout.CurveField(fieldLabel, (AnimationCurve)value);
            }
            else
            {
                EditorGUILayout.LabelField($"{fieldName}: no drawer for type {declaredType}");

                return value;
            }

            return value;
        }

        protected override object DrawPublicVariableField(string symbol, object variableValue, Type variableType, ref bool dirty, bool enabled)
        {
            bool shouldUseRuntimeValue = EditorApplication.isPlaying && currentBehaviour != null && GUI.enabled; // GUI.enabled is determined in DrawProgramSourceGUI

            EditorGUI.BeginDisabledGroup(!enabled);

            bool shouldDraw = true;
            bool isArray = variableType.IsArray;

            FieldDefinition symbolField;
            if (fieldDefinitions != null && fieldDefinitions.TryGetValue(symbol, out symbolField))
            {
                HideInInspector hideAttribute = symbolField.GetAttribute<HideInInspector>();

                if (hideAttribute != null)
                {
                    shouldDraw = false;
                }

                foreach (Attribute attribute in symbolField.fieldAttributes)
                {
                    if (attribute == null)
                        continue;

                    if (attribute is HeaderAttribute)
                    {
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField((attribute as HeaderAttribute).header, EditorStyles.boldLabel);
                    }
                    else if (attribute is SpaceAttribute)
                    {
                        GUILayout.Space((attribute as SpaceAttribute).height);
                    }
                }
            }
            else
            {
                symbolField = new FieldDefinition(null);
            }

            if (shouldDraw)
            {
                if (shouldUseRuntimeValue)
                {
                    variableValue = currentBehaviour.GetProgramVariable(symbol);
                }

                if (!isArray) // Drawing horizontal groups on arrays screws them up, there's probably better handling for this using a manual rect
                    EditorGUILayout.BeginHorizontal();

                FieldDefinition fieldDefinition = null;
                if (fieldDefinitions != null)
                    fieldDefinitions.TryGetValue(symbol, out fieldDefinition);

                EditorGUI.BeginChangeCheck();
                object newValue = DrawFieldForType(null, symbol, (variableValue, variableType, fieldDefinition), fieldDefinition != null ? fieldDefinition.fieldSymbol.userCsType : null, ref dirty, enabled);

                bool changed = EditorGUI.EndChangeCheck();

                if (changed)
                {
                    if(variableType == typeof(double)) newValue = Convert.ToDouble(newValue);

                    if (shouldUseRuntimeValue)
                    {
                        currentBehaviour.SetProgramVariable(symbol, newValue);
                    }
                    else
                    {
                        dirty = true;
                        variableValue = newValue;
                    }
                }
                
                if (symbolField.fieldSymbol != null && symbolField.fieldSymbol.syncMode != UdonSyncMode.NotSynced)
                {
                    if (symbolField.fieldSymbol.syncMode == UdonSyncMode.None)
                        GUILayout.Label("synced", GUILayout.Width(55f));
                    else
                        GUILayout.Label($"sync: {Enum.GetName(typeof(UdonSyncMode), symbolField.fieldSymbol.syncMode)}", GUILayout.Width(85f));
                }

                if (!isArray)
                {
                    object originalValue = program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(symbol));

                    if (originalValue != null && !originalValue.Equals(variableValue))
                    {
                        int originalIndent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        // Check if changed because otherwise the UI throw an error since we changed that we want to draw the undo arrow in the middle of drawing when we're modifying stuff like colors
                        if (!changed && GUI.Button(EditorGUILayout.GetControlRect(GUILayout.Width(14f), GUILayout.Height(11f)), undoArrowContent, undoLabelStyle))
                        {
                            if (shouldUseRuntimeValue)
                            {
                                currentBehaviour.SetProgramVariable(symbol, originalValue);
                            }
                            else
                            {
                                dirty = true;
                                variableValue = originalValue;
                            }
                        }
                        EditorGUI.indentLevel = originalIndent;
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUI.EndDisabledGroup();

            return variableValue;
        }

        private static readonly GUIContent noPublicVariablesLabel = new GUIContent("No public variables");

        new void DrawPublicVariables(UdonBehaviour behaviour, ref bool dirty)
        {
            IUdonVariable CreateUdonVariable(string symbolName, object value, System.Type type)
            {
                System.Type udonVariableType = typeof(UdonVariable<>).MakeGenericType(type);
                return (IUdonVariable)Activator.CreateInstance(udonVariableType, symbolName, value);
            }

            IUdonVariableTable publicVariables = null;
            if (behaviour)
                publicVariables = behaviour.publicVariables;

            EditorGUILayout.LabelField("Public Variables", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            if (program?.SymbolTable == null)
            {
                EditorGUILayout.LabelField(noPublicVariablesLabel);
                EditorGUI.indentLevel--;
                return;
            }
            
            IUdonSymbolTable symbolTable = program.SymbolTable;

            // todo: This will be removed with serialization update which has handling for updating variables on compile instead which is more reliable.
            // We'll eat the overhead of the HashSet for smaller numbers of variables in hope that it's decently faster for bigger classes
            HashSet<string> exportedSymbolNames = new HashSet<string>(symbolTable.GetExportedSymbols()); 

            if (publicVariables != null)
            {
                foreach (string variableSymbol in publicVariables.VariableSymbols.ToArray())
                {
                    if (!exportedSymbolNames.Contains(variableSymbol))
                        publicVariables.RemoveVariable(variableSymbol);
                }
            }
            // End remove block

            if (exportedSymbolNames.Count == 0)
            {
                EditorGUILayout.LabelField(noPublicVariablesLabel);
                EditorGUI.indentLevel--;
                return;
            }

            // Check if all fields are marked HideInInspector
            Dictionary<string, FieldDefinition> fields = (behaviour?.programSource as UdonSharpProgramAsset)?.fieldDefinitions;

            if (fields != null)
            {
                int hiddenCount = 0;
                foreach (string exportedSymbol in exportedSymbolNames)
                {
                    FieldDefinition fieldDef;
                    if (!fields.TryGetValue(exportedSymbol, out fieldDef))
                        continue;

                    if (fieldDef.GetAttribute<HideInInspector>() != null)
                        hiddenCount++;
                }

                if (hiddenCount >= exportedSymbolNames.Count)
                {
                    EditorGUILayout.LabelField(noPublicVariablesLabel);
                    EditorGUI.indentLevel--;
                    return;
                }
            }

            foreach (string exportedSymbol in exportedSymbolNames)
            {
                System.Type symbolType = symbolTable.GetSymbolType(exportedSymbol);
                if (publicVariables == null)
                {
                    DrawPublicVariableField(exportedSymbol, GetPublicVariableDefaultValue(exportedSymbol, null), symbolType, ref dirty, false);
                    continue;
                }

                // todo: This can be removed with serialization changes as well since the type change will be handled once by the compile fixer
                if (!publicVariables.TryGetVariableType(exportedSymbol, out System.Type declaredType) || declaredType != symbolType)
                {
                    publicVariables.RemoveVariable(exportedSymbol);
                    if (!publicVariables.TryAddVariable(CreateUdonVariable(exportedSymbol, GetPublicVariableDefaultValue(exportedSymbol, null), symbolType)))
                    {
                        EditorGUILayout.LabelField($"Error drawing field for symbol '{exportedSymbol}'");
                        continue;
                    }
                    dirty = true;
                }

                // This can also be removed
                if (!publicVariables.TryGetVariableValue(exportedSymbol, out object variableValue))
                {
                    variableValue = GetPublicVariableDefaultValue(exportedSymbol, null);
                    dirty = true;
                }

                variableValue = DrawPublicVariableField(exportedSymbol, variableValue, symbolType, ref dirty, true);
                if (!dirty)
                    continue;

                Undo.RecordObject(behaviour, "Modify variable");

                if (!publicVariables.TrySetVariableValue(exportedSymbol, variableValue))
                {
                    if (!publicVariables.TryAddVariable(CreateUdonVariable(exportedSymbol, variableValue, symbolType)))
                    {
                        Debug.LogError($"Failed to set public variable '{exportedSymbol}' value.");
                    }
                }

                EditorSceneManager.MarkSceneDirty(behaviour.gameObject.scene);

                if (PrefabUtility.IsPartOfPrefabInstance(behaviour))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
            }

            EditorGUI.indentLevel--;
        }

        protected override void OnBeforeSerialize()
        {
            UnitySerializationUtility.SerializeUnityObject(this, ref serializationData);
            base.OnBeforeSerialize();
        }

        protected override void OnAfterDeserialize()
        {
            UnitySerializationUtility.DeserializeUnityObject(this, ref serializationData);
            base.OnAfterDeserialize();
        }
    }
    
    [CustomEditor(typeof(UdonSharpProgramAsset))]
    public class UdonSharpProgramAssetEditor : UdonAssemblyProgramAssetEditor
    {
        // Allow people to drag program assets onto objects in the scene and automatically create a corresponding UdonBehaviour with everything set up
        // https://forum.unity.com/threads/drag-and-drop-scriptable-object-to-scene.546975/#post-4534333
        void OnSceneDrag(SceneView sceneView)
        {
            Event e = Event.current;
            GameObject gameObject = HandleUtility.PickGameObject(e.mousePosition, false);

            if (e.type == EventType.DragUpdated)
            {
                if (gameObject)
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                else
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;

                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                e.Use();
                
                if (gameObject)
                {
                    UdonBehaviour component = Undo.AddComponent<UdonBehaviour>(gameObject);
                    component.programSource = target as UdonSharpProgramAsset;
                }
            }
        }
    }
}