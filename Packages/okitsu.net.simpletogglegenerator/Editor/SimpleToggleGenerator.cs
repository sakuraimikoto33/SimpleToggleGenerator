using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace okitsu.net.SimpleToggleGenerator
{
    public class SimpleToggleGenerator : EditorWindow
    {
        [Serializable]
        public class ToggleGroup
        {
            public string layerName = "";
            public Texture2D groupIcon;
            public bool exclusiveMode = true;
            public bool allowDisableAll = false;
            public List<GameObject> objects = new();
            public bool isFoldout = true;
            public List<bool> isSettingsFoldout = new();
            public List<bool> save = new();
            public List<bool> sync = new();
            public List<Texture2D> propIcon = new();
            public List<string> customNames = new();
            public List<string> parameterNames = new();
            [NonSerialized] public UnityEditorInternal.ReorderableList reorderableList;
        }

        private List<ToggleGroup> _toggleGroups = new();
        private UnityEditorInternal.ReorderableList _groupReorderableList;
        private string _savePath;
        private VRCAvatarDescriptor _avatar;
        private void OnEnable()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            SearchForAvatar();
            SetupGroupReorderableList();
        }
        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            SearchForAvatar();
        }
        private void SearchForAvatar()
        {
            // _avatar が未設定の場合、VRCAvatarDescriptor を探す
            if (_avatar == null)
            {
                _avatar = FindObjectOfType<VRCAvatarDescriptor>();

                if (_avatar != null)
                {
                    Debug.Log($"Found VRCAvatarDescriptor: {_avatar.name}");
                    Repaint();
                }
                else
                {
                    Debug.LogWarning("VRCAvatarDescriptor not found in the scene.");
                }
            }
        }
        private AnimatorController _animatorController;
        private VRCExpressionsMenu _vrcExpressionsMenu;
        private VRCExpressionsMenu _rootMenu;
        private string _rootMenuName = "";
        private VRCExpressionParameters _vrcExpressionParameters;
        private bool _foldoutMenu = false;
        private bool _disablecfmdialog
        {
            get { return EditorPrefs.GetBool("DisableCfmDialog", false); }
            set { EditorPrefs.SetBool("DisableCfmDialog", value); }
        }

        [MenuItem("Tools/Simple Toggle Generator")]
        public static void ShowWindow()
        {
            GetWindow<SimpleToggleGenerator>("Simple Toggle Generator");
        }

        private Vector2 _scrollPosition;
        private VRCAvatarDescriptor _previousAvatar;
        
        // ====GUI要素====
        private void OnGUI()
        {
            GUILayout.Label("Save Path", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            // Save Path
            _savePath = EditorGUILayout.TextField(new GUIContent("Save Path", "If Avatar is specified, AnimationClip will be saved in AvatarName/LayerName/. If not specified, it will be saved in AnimatorControllerName/Layer Name/."), _savePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string newSavePath = EditorUtility.OpenFolderPanel("Select Save Path", Application.dataPath, "");
                if (!string.IsNullOrEmpty(newSavePath))
                {
                    _savePath = "Assets" + newSavePath.Substring(Application.dataPath.Length);
                }
            }
            GUILayout.EndHorizontal();

            _avatar = EditorGUILayout.ObjectField("Avatar (Required)", _avatar, typeof(VRCAvatarDescriptor), true) as VRCAvatarDescriptor;

            // VRCAvatarDescriptorの割り当てまたは変更時にAnimatorController/VRCExpressionsMenu/VRCExpressionParametersを自動的に割り当てる
            if (_avatar != _previousAvatar)
            {
                _previousAvatar = _avatar;

                if (_avatar != null)
                {
                    // FXLayerが設定されているかを確認
                    if (_avatar.baseAnimationLayers[4].animatorController != null)
                    {
                        _animatorController = (AnimatorController)_avatar.baseAnimationLayers[4].animatorController;
                    }
                    else
                    {
                        Debug.LogWarning("FXLayer is not assigned in the AvatarDescriptor's baseAnimationLayers.");
                        _animatorController = null;
                    }

                    // VRCExpressionsMenuが設定されているかを確認
                    if (_avatar.expressionsMenu != null)
                    {
                        _vrcExpressionsMenu = _avatar.expressionsMenu;
                    }
                    else
                    {
                        Debug.LogWarning("ExMenu is not assigned to AvatarDescriptor.");
                        _vrcExpressionsMenu = null;
                    }

                    // VRCExpressionParametersが設定されているかを確認
                    if (_avatar.expressionParameters != null)
                    {
                        _vrcExpressionParameters = _avatar.expressionParameters;
                    }
                    else
                    {
                        Debug.LogWarning("EXParam is not assigned to AvatarDescriptor.");
                        _vrcExpressionParameters = null;
                    }
                }
                else
                {
                    _animatorController = null;
                    _vrcExpressionsMenu = null;
                    _vrcExpressionParameters = null;
                }
            }

            if (_avatar == null)
            {
                _vrcExpressionsMenu = null;
                _vrcExpressionParameters = null;
            }

            // FXLayer
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_avatar != null && _avatar.baseAnimationLayers[4].animatorController != null);
            _animatorController = EditorGUILayout.ObjectField("FXLayer", _animatorController, typeof(AnimatorController), false) as AnimatorController;
            if (_avatar != null && _animatorController == null && _avatar.baseAnimationLayers[4].animatorController == null)
            {
                if (GUILayout.Button("Create FXlayer", GUILayout.Width(100)))
                {
                    if (!_avatar.customizeAnimationLayers)
                    {
                        _avatar.customizeAnimationLayers = true;
                    }
                    if (_avatar.baseAnimationLayers[4].isDefault)
                    {
                        _avatar.baseAnimationLayers[4].isDefault = false;
                    }

                    var controller = AnimatorController.CreateAnimatorControllerAtPath(_savePath + "/" + "FX_" + _avatar.name + ".controller");

                    _avatar.baseAnimationLayers[4].animatorController = (RuntimeAnimatorController)AssetDatabase.LoadAssetAtPath(_savePath + "/" + "FX_" + _avatar.name + ".controller", typeof(RuntimeAnimatorController));
                    _animatorController = (AnimatorController)_avatar.baseAnimationLayers[4].animatorController;
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            // ExMenu
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_avatar != null && _avatar.expressionsMenu != null);
            _vrcExpressionsMenu = EditorGUILayout.ObjectField("ExMenu", _vrcExpressionsMenu, typeof(VRCExpressionsMenu), false) as VRCExpressionsMenu;
            if (_avatar != null && _vrcExpressionsMenu == null && _avatar.expressionsMenu == null)
            {
                if (GUILayout.Button("Create ExMenu", GUILayout.Width(100)))
                {
                    if (!_avatar.customExpressions)
                    {
                        _avatar.customExpressions = true;
                    }
                    VRCExpressionsMenu expressionsMenu = CreateInstance<VRCExpressionsMenu>();
                    AssetDatabase.CreateAsset(expressionsMenu, _savePath + "/" + "ExMenu_" + _avatar.name + ".asset");
                    _avatar.expressionsMenu = AssetDatabase.LoadAssetAtPath(_savePath + "/" + "ExMenu_" + _avatar.name + ".asset", typeof(VRCExpressionsMenu)) as VRCExpressionsMenu;
                    _vrcExpressionsMenu = _avatar.expressionsMenu;
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
            // RootMenu フィールドを追加
            _rootMenu = EditorGUILayout.ObjectField("RootMenu", _rootMenu, typeof(VRCExpressionsMenu), false) as VRCExpressionsMenu;

            // RootMenu が null の場合のみ RootMenuName を表示
            if (_rootMenu == null && _vrcExpressionsMenu != null)
            {
                _rootMenuName = EditorGUILayout.TextField(
                    new GUIContent("RootMenuName", ""), 
                    string.IsNullOrEmpty(_rootMenuName) ? "Simple Toggle Menu" : _rootMenuName
                );
            }

            // ExParam
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_avatar != null && _avatar.expressionParameters != null);
            _vrcExpressionParameters = EditorGUILayout.ObjectField("ExParam", _vrcExpressionParameters, typeof(VRCExpressionParameters), false) as VRCExpressionParameters;
            if (_avatar != null && _vrcExpressionParameters == null && _avatar.expressionParameters == null)
            {
                if (GUILayout.Button("Create ExParam", GUILayout.Width(100)))
                {
                    if (!_avatar.customExpressions)
                    {
                        _avatar.customExpressions = true;
                    }
                    VRCExpressionParameters expressionParameters = CreateInstance<VRCExpressionParameters>();
                    AssetDatabase.CreateAsset(expressionParameters, _savePath + "/" + "ExParan_" + _avatar.name + ".asset");
                    _avatar.expressionParameters = AssetDatabase.LoadAssetAtPath(_savePath + "/" + "ExParan_" + _avatar.name + ".asset", typeof(VRCExpressionParameters)) as VRCExpressionParameters;
                    _vrcExpressionParameters = _avatar.expressionParameters;
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            // GUILayout.Space(10);

            _foldoutMenu = EditorGUILayout.Foldout(_foldoutMenu, "Additional Options");
            if (_foldoutMenu)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Disable Confirm dialog", GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                _disablecfmdialog = EditorGUILayout.Toggle(_disablecfmdialog);
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Toggle Groups", EditorStyles.boldLabel);

            if (GUILayout.Button("Add Group"))
            {
                var newGroup = new ToggleGroup();
                SetupReorderableList(newGroup);
                _toggleGroups.Add(newGroup);
            }

            GUILayout.Space(5);

            if (GUILayout.Button("Clear All Groups"))
            {
                if (_toggleGroups.Count > 0)
                {
                    bool checkClear = EditorUtility.DisplayDialog("Clear All Groups", "Clear all groups. Are you sure?", "Yes", "Cancel");
                    if (checkClear)
                    {
                        _toggleGroups.Clear();
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (GUILayout.Button("Generate Toggle"))
            {
                foreach (var group in _toggleGroups)
                {
                    if (!group.objects.Any(obj => obj != null && obj.activeSelf) && group.objects.Count > 0 && !group.allowDisableAll)
                    {
                        group.objects[0].SetActive(true);
                    }
                }
                GenerateToggle();
            }

            GUILayout.Space(10);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            _groupReorderableList.DoLayoutList();
            EditorGUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                SaveToggleGroups();
            }
            if (GUILayout.Button("Load"))
            {
                LoadToggleGroups();
            }
            GUILayout.EndHorizontal();
        }

        private void SetupGroupReorderableList()
        {
            _groupReorderableList = new UnityEditorInternal.ReorderableList(_toggleGroups, typeof(ToggleGroup), true, true, true, true);

            _groupReorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Toggle Groups (Drag to reorder)");
            };

            _groupReorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= _toggleGroups.Count) return;
                var group = _toggleGroups[index];

                // --- 共通寸法 ---
                float lh = UIStyles.LineHeight;            // 1行の高さ
                float vs = UIStyles.VerticalSpacing;       // 行間
                float padding = 6f;                        // 枠内の左右余白
                float xL = rect.x + padding;               // 左端（余白込み）
                float wL = rect.width - (padding * 2);     // 有効幅（余白込み）
                float yplus  = rect.y + 1;

                // --- 背景（helpBox） ---
                GUI.Box(new Rect(rect.x, rect.y, rect.width, rect.height), GUIContent.none, EditorStyles.helpBox);

                // --- ヘッダー：Foldout（三角マークのみ） + ラベル + Remove Group ---
                Rect foldoutRect = new Rect(xL, yplus, 16, lh); 
                group.isFoldout = EditorGUI.Foldout(foldoutRect, group.isFoldout, GUIContent.none, false);

                // ラベル部分（ドラッグ用）
                // Foldout の右にラベルだけ表示する。ここは開閉しないのでドラッグに使える
                Rect labelRect = new Rect(xL + 18, yplus, wL - 142, lh);
                EditorGUI.LabelField(labelRect, group.layerName);

                // Remove Group ボタン
                if (GUI.Button(new Rect(rect.x + rect.width - padding - 120, yplus + 2, 120, lh), "Remove Group"))
                {
                    _toggleGroups.RemoveAt(index);
                    return;
                }
                yplus += lh + vs;

                if (group.isFoldout)
                {
                    EditorGUI.indentLevel++;

                    // Menu Name
                    group.layerName = EditorGUI.TextField(
                        new Rect(xL, yplus, wL, lh),
                        "Menu Name", string.IsNullOrEmpty(group.layerName) ? "New Layer" : group.layerName
                    );
                    yplus += lh + vs;

                    // Group Icon
                    group.groupIcon = (Texture2D)EditorGUI.ObjectField(
                        new Rect(xL, yplus, wL, EditorGUIUtility.singleLineHeight),
                        "Group Icon", group.groupIcon, typeof(Texture2D), false);
                    yplus += lh + vs;

                    // Exclusive Mode
                    group.exclusiveMode = EditorGUI.Toggle(
                        new Rect(xL, yplus, wL, lh),
                        "Exclusive Mode", group.exclusiveMode
                    );
                    yplus += lh + vs;

                    // AllowDisableAll（Exclusive が false のとき無効）
                    EditorGUI.BeginDisabledGroup(!group.exclusiveMode);
                    group.allowDisableAll = EditorGUI.Toggle(
                        new Rect(xL, yplus, wL, lh),
                        "AllowDisableAll", group.allowDisableAll
                    );
                    EditorGUI.EndDisabledGroup();
                    yplus += lh + vs;

                    // ReorderableList（オブジェクト一覧）
                    if (group.reorderableList == null)
                        SetupReorderableList(group);

                    Rect listRect = new Rect(xL, yplus, wL, group.reorderableList.GetHeight());
                    group.reorderableList.DoList(listRect);
                    yplus += group.reorderableList.GetHeight() + vs;

                    // Add / Clear Buttons
                    const float btnW = 120f;
                    const float gap  = 10f;

                    if (GUI.Button(new Rect(xL, yplus, btnW, lh), "Add Object"))
                    {
                        GameObject[] selectedObjects = Selection.gameObjects;
                        foreach (var obj in selectedObjects)
                        {
                            bool isAlreadyAdded = _toggleGroups.Any(g => g.objects.Contains(obj));
                            if (!isAlreadyAdded && !group.objects.Contains(obj))
                            {
                                group.objects.Add(obj);
                                group.save.Add(true);
                                group.sync.Add(true);
                                group.propIcon.Add(null);
                                group.parameterNames.Add("");
                                group.customNames.Add("");
                            }
                        }
                    }

                    if (GUI.Button(new Rect(xL + btnW + gap, yplus, btnW, lh), "Clear All Objects"))
                    {
                        group.objects.Clear();
                        group.save.Clear();
                        group.sync.Clear();
                        group.propIcon.Clear();
                        group.parameterNames.Clear();
                        group.customNames.Clear();
                        group.isSettingsFoldout.Clear();
                    }
                    yplus += lh + vs;

                    // Drag & Drop Area
                    Rect dropArea = new Rect(xL, yplus, wL, UIStyles.DropAreaHeight);
                    GUI.Box(dropArea, "Drag & Drop GameObjects Here");

                    Event evt = Event.current;
                    switch (evt.type)
                    {
                        case EventType.DragUpdated:
                        case EventType.DragPerform:
                            if (!dropArea.Contains(evt.mousePosition))
                                break;

                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                            if (evt.type == EventType.DragPerform)
                            {
                                DragAndDrop.AcceptDrag();

                                foreach (var draggedObject in DragAndDrop.objectReferences)
                                {
                                    if (draggedObject is GameObject draggedGO)
                                    {
                                        bool isAlreadyAdded = _toggleGroups.Any(g => g.objects.Contains(draggedGO));
                                        if (!isAlreadyAdded && !group.objects.Contains(draggedGO))
                                        {
                                            group.objects.Add(draggedGO);
                                            group.save.Add(true);
                                            group.sync.Add(true);
                                            group.propIcon.Add(null);
                                            group.parameterNames.Add("");
                                            group.customNames.Add("");
                                        }
                                        else
                                        {
                                            Debug.LogWarning($"Object {draggedGO.name} is already added to another group.");
                                        }
                                    }
                                }
                            }
                            Event.current.Use();
                            break;
                    }

                    EditorGUI.indentLevel--;
                }
            };

            _groupReorderableList.elementHeightCallback = (int index) =>
            {
                if (index >= _toggleGroups.Count) return UIStyles.LineHeight;
                var group = _toggleGroups[index];
                float height = UIStyles.LineHeight + UIStyles.VerticalSpacing; // Foldout

                if (group.isFoldout)
                {
                    // 基本設定 4行 (Menu, Icon, Exclusive, AllowDisableAll)
                    height += UIStyles.GetLines(4);

                    // ReorderableList の高さ
                    if (group.reorderableList != null)
                        height += group.reorderableList.GetHeight() + UIStyles.VerticalSpacing;

                    // Add/Clear ボタン行
                    height += UIStyles.ButtonRowHeight;

                    // Drag & Drop エリア
                    height += UIStyles.DropAreaHeight + UIStyles.VerticalSpacing;
                }

                return height;
            };

            _groupReorderableList.onAddCallback = (list) =>
            {
                var newGroup = new ToggleGroup();
                SetupReorderableList(newGroup);
                _toggleGroups.Add(newGroup);
            };

            _groupReorderableList.onRemoveCallback = (list) =>
            {
                if (list.index >= 0 && list.index < _toggleGroups.Count)
                    _toggleGroups.RemoveAt(list.index);
            };
        }

        private void SetupReorderableList(ToggleGroup group)
        {
            group.reorderableList = new UnityEditorInternal.ReorderableList(group.objects, typeof(GameObject), true, true, true, true);

            group.reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Objects (Drag to Reorder)");
            };

            group.reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index >= group.objects.Count) return;

                float lineHeight = EditorGUIUtility.singleLineHeight + 2;

                // --- デフォルトチェックボックス + ObjectField ---
                float toggleWidth = 20f;
                Rect toggleRect = new Rect(rect.x, rect.y, toggleWidth, EditorGUIUtility.singleLineHeight);
                Rect objRect = new Rect(rect.x + toggleWidth, rect.y, rect.width - toggleWidth, EditorGUIUtility.singleLineHeight);

                bool isChecked = GUI.Toggle(toggleRect, group.objects[index] != null && group.objects[index].activeSelf, GUIContent.none);
                if (isChecked)
                {
                    for (int k = 0; k < group.objects.Count; k++)
                    {
                        if (group.objects[k] != null)
                            group.objects[k].SetActive(k == index);
                    }
                }

                group.objects[index] = (GameObject)EditorGUI.ObjectField(objRect, group.objects[index], typeof(GameObject), true);

                // --- Settings Foldout ---
                while (group.isSettingsFoldout.Count <= index)
                    group.isSettingsFoldout.Add(false);

                string settingsLabel = !string.IsNullOrEmpty(group.customNames[index])
                    ? group.customNames[index]
                    : (group.objects[index] != null ? group.objects[index].name : "Settings");

                Rect foldoutRect = new Rect(rect.x, rect.y + lineHeight, rect.width, EditorGUIUtility.singleLineHeight);
                group.isSettingsFoldout[index] = EditorGUI.Foldout(foldoutRect, group.isSettingsFoldout[index], $"Settings ({settingsLabel})");

                if (group.isSettingsFoldout[index])
                {
                    EditorGUI.indentLevel++;
                    float y = rect.y + lineHeight * 2;

                    // Save
                    group.save[index] = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "Save", index < group.save.Count ? group.save[index] : true);
                    y += lineHeight;

                    // Sync
                    group.sync[index] = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "Sync", index < group.sync.Count ? group.sync[index] : true);
                    y += lineHeight;

                    // Prop Icon
                    group.propIcon[index] = (Texture2D)EditorGUI.ObjectField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "Prop Icon", group.propIcon[index], typeof(Texture2D), false);
                    y += lineHeight;

                    // Custom Name
                    group.customNames[index] = EditorGUI.TextField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "InMenu Name", group.customNames[index]);
                    y += lineHeight;

                    // Parameter Name
                    group.parameterNames[index] = EditorGUI.TextField(new Rect(rect.x, y, rect.width, EditorGUIUtility.singleLineHeight), "Parameter Name", group.parameterNames[index]);
                    y += lineHeight;

                    EditorGUI.indentLevel--;
                }
            };

            // 高さ計算を修正
            group.reorderableList.elementHeightCallback = (int index) =>
            {
                float lineHeight = EditorGUIUtility.singleLineHeight + 2;
                float height = lineHeight * 2; // ObjectField + Foldout

                if (index < group.isSettingsFoldout.Count && group.isSettingsFoldout[index])
                {
                    height += (lineHeight * 5); // Save, Sync, Prop Icon, Custom Name, Parameter Name
                }
                return height;
            };

            // Add ボタン
            group.reorderableList.onAddCallback = (UnityEditorInternal.ReorderableList list) =>
            {
                group.objects.Add(null);
                group.save.Add(true);
                group.sync.Add(true);
                group.propIcon.Add(null);
                group.parameterNames.Add("");
                group.customNames.Add("");
                group.isSettingsFoldout.Add(false);
            };

            // Remove ボタン
            group.reorderableList.onRemoveCallback = (UnityEditorInternal.ReorderableList list) =>
            {
                int index = list.index;
                if (index >= 0 && index < group.objects.Count)
                {
                    group.objects.RemoveAt(index);
                    group.save.RemoveAt(index);
                    group.sync.RemoveAt(index);
                    group.propIcon.RemoveAt(index);
                    group.parameterNames.RemoveAt(index);
                    group.customNames.RemoveAt(index);
                    group.isSettingsFoldout.RemoveAt(index);
                }
            };
        }

        private static class UIStyles
        {
            public const float LineHeight = 20f;   // = EditorGUIUtility.singleLineHeight
            public const float VerticalSpacing = 4f;
            public const float DropAreaHeight = 60f;
            public const float ButtonRowHeight = LineHeight + VerticalSpacing;

            public static float GetLines(int count)
            {
                return (LineHeight + VerticalSpacing) * count;
            }
        }

        // ====実行ボタン====
        private void GenerateToggle()
        {
            // 続行する前にグループを検証
            if (!ValidateToggleGroups())
            {
                Debug.LogError("Verification failed");
                return;
            }

            // レイヤー名の重複チェック
            bool CheckDuplicateLayerNames()
            {
                List<string> toggleGroupLayerNames = new List<string>();
                foreach (var group in _toggleGroups)
                {
                    toggleGroupLayerNames.Add(group.layerName);
                }

                foreach (var layer in _animatorController.layers)
                {
                    if (toggleGroupLayerNames.Contains(layer.name))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (!_disablecfmdialog && CheckDuplicateLayerNames())
            {
                string messageLine1 = "Duplicate layer names detected in AnimatorController.\n";
                string messageLine2 = "Please overwrite or cancel and change the layer name.";

                bool overwrite = EditorUtility.DisplayDialog("Confirm", messageLine1 + messageLine2, "Continue", "Cancel");

                if (!overwrite)
                {
                    Debug.Log("Processing has been canceled.");
                    return;
                }
            }

            foreach (var toggleGroup in _toggleGroups)
            {
                GenerateLayerAndClip(toggleGroup);
            }

            if (_vrcExpressionsMenu == null)
            {
                Debug.LogError("ExpressionsMenu are not set on the AvatarDescriptor. Cannot add expressions menu.");
                return;
            }
            else
            {
                CreateExpressionMenus();
            }
            if (_vrcExpressionParameters == null)
            {
                Debug.LogError("ExpressionParameters are not set on the AvatarDescriptor. Cannot add expression parameters.");
                return;
            }
            else
            {
                AddExpressionParameters();
            }

            // Save Assets
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Animation clips generated and added to Animator Controller: " + _animatorController.name);
        }

        // ====検証====
        private bool ValidateToggleGroups()
        {
            if (_toggleGroups.Count == 0)
            {
                Debug.LogError("No script groups defined. Please add at least one script group.");
                return false;
            }
            if (_avatar == null)
            {
                Debug.LogError("No Avatar selected.");
                return false;
            }

            foreach (var toggleGroup in _toggleGroups)
            {
                if (toggleGroup.objects.Count < 2)
                {
                    Debug.LogError("At least 2 game objects are required to generate animation clips in script group '" + toggleGroup.layerName + "'.");
                    return false;
                }
                if (string.IsNullOrEmpty(toggleGroup.layerName))
                {
                    Debug.LogError("Layer name is required for generating animation clips.");
                    return false;
                }

                // === 非排他モード用チェック ===
                if (!toggleGroup.exclusiveMode)
                {
                    List<(string paramName, AnimatorControllerParameterType type)> invalidParams = new();

                    foreach (var paramName in toggleGroup.parameterNames)
                    {
                        if (string.IsNullOrEmpty(paramName)) continue;

                        var existingParam = _animatorController.parameters.FirstOrDefault(p => p.name == paramName);
                        if (existingParam != null && existingParam.type != AnimatorControllerParameterType.Float)
                        {
                            invalidParams.Add((paramName, existingParam.type));
                        }
                    }

                    if (invalidParams.Count > 0)
                    {
                        string paramList = string.Join("\n", invalidParams.Select(p => $"- {p.paramName} (type: {p.type})"));
                        string message =
                            $"The following parameters already exist with a non-Float type:\n\n{paramList}\n\n" +
                            $"Non-exclusive mode requires Float parameters.\n\n" +
                            $"How would you like to proceed?";

                        int option = EditorUtility.DisplayDialogComplex(
                            "Parameter Type Warning",
                            message,
                            "Continue Anyway",             // 0
                            "Cancel",                      // 1
                            "Replace with Float Parameters"// 2
                        );

                        if (option == 1) // Cancel
                        {
                            Debug.Log("Process canceled by user due to invalid parameter types.");
                            return false;
                        }
                        else if (option == 2) // Replace with Float Parameters
                        {
                            // === 他レイヤー使用チェック ===
                            List<string> usedParams = new();

                            foreach (var (paramName, _) in invalidParams)
                            {
                                foreach (var layer in _animatorController.layers)
                                {
                                    if (layer.name == toggleGroup.layerName) continue;

                                    if (IsParameterUsedInStateMachine(layer.stateMachine, paramName))
                                    {
                                        if (!usedParams.Contains(paramName))
                                            usedParams.Add(paramName);
                                    }
                                }
                            }

                            if (usedParams.Count > 0)
                            {
                                string usedList = string.Join("\n", usedParams.Select(p => $"- {p}"));
                                string warnMsg =
                                    $"The following parameters are used as transition conditions in other layers:\n\n{usedList}\n\n" +
                                    $"Replacing them with Float parameters may break existing transitions.\n\n" +
                                    $"Do you want to continue?";

                                bool cont = EditorUtility.DisplayDialog(
                                    "Parameter Usage Warning",
                                    warnMsg,
                                    "Continue Anyway",
                                    "Cancel"
                                );

                                if (!cont)
                                {
                                    Debug.Log("Process canceled due to parameter usage in other layers.");
                                    return false;
                                }
                            }

                            // === 問題なければ削除 ===
                            foreach (var (paramName, paramType) in invalidParams)
                            {
                                var toRemove = _animatorController.parameters.FirstOrDefault(p => p.name == paramName);
                                if (toRemove != null)
                                {
                                    _animatorController.RemoveParameter(toRemove);
                                    Debug.Log($"Removed parameter '{paramName}' of type {paramType}. A Float parameter will be created later.");
                                }
                            }
                        }
                        // option == 0 → Continue Anyway（そのまま進める）
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// サブステートマシンも含めてパラメータ使用チェック
        /// </summary>
        private bool IsParameterUsedInStateMachine(AnimatorStateMachine stateMachine, string paramName)
        {
            // ステートの遷移を確認
            foreach (var state in stateMachine.states)
            {
                foreach (var transition in state.state.transitions)
                {
                    foreach (var condition in transition.conditions)
                    {
                        if (condition.parameter == paramName)
                        {
                            return true;
                        }
                    }
                }
            }

            // サブステートマシンの遷移を確認
            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                if (IsParameterUsedInStateMachine(subStateMachine.stateMachine, paramName))
                    return true;
            }

            // エントリー/AnyStateなどの遷移
            foreach (var transition in stateMachine.anyStateTransitions)
            {
                foreach (var condition in transition.conditions)
                {
                    if (condition.parameter == paramName)
                        return true;
                }
            }
            foreach (var transition in stateMachine.entryTransitions)
            {
                foreach (var condition in transition.conditions)
                {
                    if (condition.parameter == paramName)
                        return true;
                }
            }

            return false;
        }

        // ====コア====
        private void GenerateLayerAndClip(ToggleGroup toggleGroup)
        {
            // 同名のレイヤーが既に存在するかチェック
            AnimatorControllerLayer existingLayer = _animatorController.layers.FirstOrDefault(layer => layer.name == toggleGroup.layerName);

            if (existingLayer != null)
            {
                // 同名のレイヤーを削除
                AnimatorControllerLayer[] updatedLayers = _animatorController.layers.Where(layer => layer.name != toggleGroup.layerName).ToArray();
                _animatorController.layers = updatedLayers;
                Debug.Log($"Overwritten the {_animatorController} layer. An unintended problem may have occurred.");
            }

            // 既存のパラメーター名を保存
            HashSet<string> existingParameterNames = new();
            foreach (var parameter in _animatorController.parameters)
            {
                existingParameterNames.Add(parameter.name);
            }
            if (toggleGroup.exclusiveMode)
            {
                // 新しいレイヤーを作成
                AnimatorControllerLayer newLayer = new()
                {
                    name = toggleGroup.layerName,
                    stateMachine = new AnimatorStateMachine
                    {
                        name = toggleGroup.layerName,
                        hideFlags = HideFlags.HideInHierarchy
                    }
                };
                AssetDatabase.AddObjectToAsset(newLayer.stateMachine, _animatorController);

                // AnimationClipを作成してStateの設定を
                Dictionary<GameObject, AnimatorState> stateDictionary = new();
                foreach (var obj in toggleGroup.objects)
                {
                    string stateName = toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj)];
                    AnimatorState state = newLayer.stateMachine.AddState(stateName);

                    state.motion = CreateAnimationClip(obj, toggleGroup.objects, toggleGroup);
                    state.writeDefaultValues = false;
                    stateDictionary.Add(obj, state);

                    // チェックボックスが有効なオブジェクトが存在しない場合の処理
                    if (obj.activeSelf && !toggleGroup.allowDisableAll)
                    {
                        newLayer.stateMachine.defaultState = state;
                    }
                }

                AnimatorState allDisabledState = null;
                if (toggleGroup.allowDisableAll)
                {
                    allDisabledState = newLayer.stateMachine.AddState("AllDisabled");
                    // newLayer.stateMachine.defaultState = allDisabledState;
                    allDisabledState.motion = CreateDisableAllAnimationClip(toggleGroup.objects, toggleGroup);
                }

                // VRCAvatarParameterDriverをすべてのStateに追加
                foreach (var state in newLayer.stateMachine.states.Select(s => s.state))
                {
                    // VRCAvatarParameterDriverをすべてのStateに追加
                    VRCAvatarParameterDriver driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    // Configure driver settings
                    driver.parameters = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>();
                    driver.localOnly = true;

                    // VRCAvatarParameterDriverのパラメータを設定
                    foreach (var param in toggleGroup.parameterNames)
                    {
                        VRC.SDKBase.VRC_AvatarParameterDriver.Parameter driverParam = new()
                        {
                            name = param
                        };

                        // StateがDefault Stateか確認
                        if (state != newLayer.stateMachine.defaultState)
                        {
                            // Default Stateでない場合、自己遷移条件を除くすべてのパラメータを設定
                            if (param != state.name)
                            {
                                driverParam.value = 0;
                                driverParam.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
                                driver.parameters.Add(driverParam);
                            }
                        }
                        else
                        {
                            // Default Stateの場合、すべての遷移条件のパラメーターを設定
                            if (param != state.name)
                            {
                                driverParam.value = 0;
                                driverParam.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
                            }
                            else
                            {
                                driverParam.value = 1;
                                driverParam.type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set;
                            }
                            driver.parameters.Add(driverParam);
                        }
                    }
                }

                // State間の遷移を設定
                foreach (var obj1 in toggleGroup.objects)
                {
                    foreach (var obj2 in toggleGroup.objects)
                    {
                        if (obj1 != obj2)
                        {
                            // transitionを作成
                            AnimatorStateTransition transition = stateDictionary[obj1].AddTransition(stateDictionary[obj2]);
                            transition.hasExitTime = false;
                            transition.exitTime = 0f;
                            transition.duration = 0f;
                            transition.interruptionSource = TransitionInterruptionSource.None;

                            // parameterNamesが既存のパラメーター名に無い場合パラメーターを追加
                            string paramName = toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj2)];
                            if (!existingParameterNames.Contains(paramName))
                            {
                                var parameter = new AnimatorControllerParameter
                                {
                                    name = paramName,
                                    type = AnimatorControllerParameterType.Bool
                                };
                                _animatorController.AddParameter(parameter);
                                existingParameterNames.Add(paramName);
                            }

                            // 遷移条件を設定
                            transition.AddCondition(AnimatorConditionMode.If, 0, paramName);
                        }
                    }
                }

                // Default State以外のすべてのStateからDefault Stateへの遷移を設定
                foreach (var nonDefaultState in newLayer.stateMachine.states)
                {
                    // Stateが存在し、Default Stateでないかチェック
                    if (nonDefaultState.state != null && nonDefaultState.state != newLayer.stateMachine.defaultState)
                    {
                        // Default Stateへの遷移を追加
                        AnimatorStateTransition defaultTransition = nonDefaultState.state.AddTransition(newLayer.stateMachine.defaultState);
                        defaultTransition.hasExitTime = false;
                        defaultTransition.duration = 0f;
                        defaultTransition.exitTime = 0f;
                        defaultTransition.interruptionSource = TransitionInterruptionSource.None;

                        // すべてのパラメーターがfalseになった時に、Default Stateへ戻るように遷移条件を設定
                        foreach (var param in toggleGroup.parameterNames)
                        {
                            defaultTransition.AddCondition(AnimatorConditionMode.IfNot, 0, param);
                        }
                    }
                }

                // AllDisabledから各Stateへの遷移を追加
                if (allDisabledState != null)
                {
                    foreach (var state in stateDictionary.Values)
                    {
                        AnimatorStateTransition transition = allDisabledState.AddTransition(state);
                        transition.hasExitTime = false;
                        transition.exitTime = 0f;
                        transition.duration = 0f;
                        transition.interruptionSource = TransitionInterruptionSource.None;

                        string paramName = toggleGroup.parameterNames.First(p => p == state.name);
                        transition.AddCondition(AnimatorConditionMode.If, 1, paramName);
                    }
                }
                _animatorController.AddLayer(newLayer);
            }
            else
            {
                // === 非排他モード（DBT方式：親BlendTree + 子BlendTree） ===
                AnimatorControllerLayer newLayer = new()
                {
                    name = toggleGroup.layerName,
                    stateMachine = new AnimatorStateMachine
                    {
                        name = toggleGroup.layerName,
                        hideFlags = HideFlags.HideInHierarchy
                    }
                };
                AssetDatabase.AddObjectToAsset(newLayer.stateMachine, _animatorController);

                // 親用パラメータを作成 (DBT/MenuName) デフォルト値 = 1
                string parentParam = $"DBT/{toggleGroup.layerName}";
                if (!_animatorController.parameters.Any(p => p.name == parentParam))
                {
                    var parentParameter = new AnimatorControllerParameter
                    {
                        name = parentParam,
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = 1f
                    };
                    _animatorController.AddParameter(parentParameter);
                }

                // 親BlendTree (Direct)
                BlendTree parentBlendTree = new BlendTree
                {
                    name = $"{toggleGroup.layerName}_ToggleStateBlendTree",
                    blendType = BlendTreeType.Direct,
                    useAutomaticThresholds = false
                };
                AssetDatabase.AddObjectToAsset(parentBlendTree, _animatorController);

                var parentChildren = new List<ChildMotion>();

                // 各オブジェクトごとに子BlendTreeを作成
                for (int i = 0; i < toggleGroup.objects.Count; i++)
                {
                    var obj = toggleGroup.objects[i];
                    string paramName = toggleGroup.parameterNames[i];

                    // オブジェクト個別のパラメータ (Float, 0=Off, 1=On)
                    if (!_animatorController.parameters.Any(p => p.name == paramName))
                    {
                        _animatorController.AddParameter(paramName, AnimatorControllerParameterType.Float);
                    }

                    // 子BlendTree (1D)
                    BlendTree childTree = new BlendTree
                    {
                        name = $"{obj.name}_BlendTree",
                        blendType = BlendTreeType.Simple1D,
                        useAutomaticThresholds = false,
                        blendParameter = paramName
                    };
                    AssetDatabase.AddObjectToAsset(childTree, _animatorController);

                    // OffClip (threshold 0) / OnClip (threshold 1)
                    AnimationClip offClip = CreateSingleDisableClip(obj, toggleGroup);
                    AnimationClip onClip = CreateSingleEnableClip(obj, toggleGroup);

                    childTree.AddChild(offClip, 0f);
                    childTree.AddChild(onClip, 1f);

                    // 親BlendTreeに子BlendTreeを追加
                    parentChildren.Add(new ChildMotion
                    {
                        motion = childTree,
                        directBlendParameter = parentParam
                    });
                }

                parentBlendTree.children = parentChildren.ToArray();

                // 親BlendTreeを持つステートを作成
                AnimatorState mainState = newLayer.stateMachine.AddState($"{toggleGroup.layerName} (WD On)");
                mainState.motion = parentBlendTree;

                // ★ Write Defaults を有効化
                mainState.writeDefaultValues = true;

                newLayer.stateMachine.defaultState = mainState;

                _animatorController.AddLayer(newLayer);
            }
        }

        // ====排他モードでのAnimation作成====
        private AnimationClip CreateAnimationClip(GameObject obj, List<GameObject> groupObjects, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = $"{obj.name}_Enabled";

            // オブジェクトを有効化
            clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 1f));

            // 同じグループの他オブジェクトを無効化
            foreach (var otherObj in groupObjects)
            {
                if (otherObj != obj)
                {
                    clip.SetCurve(GetGameObjectPath(otherObj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 0f));
                }
            }

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.layerName);
        }

        private AnimationClip CreateDisableAllAnimationClip(List<GameObject> groupObjects, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = "AllDisabled";

            // 全オブジェクト無効化
            foreach (var obj in groupObjects)
            {
                clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 0f));
            }

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.layerName);
        }

        // ====非排他モードでのAnimation作成====
        private AnimationClip CreateSingleEnableClip(GameObject obj, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = $"{obj.name}_Enable";

            // オブジェクトを有効化
            clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 1f));

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.layerName);
        }

        private AnimationClip CreateSingleDisableClip(GameObject obj, ToggleGroup toggleGroup)
        {
            AnimationClip clip = new();
            clip.name = $"{obj.name}_Disable";

            // オブジェクトを無効化
            clip.SetCurve(GetGameObjectPath(obj), typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 0f, 0f));

            clip.wrapMode = WrapMode.Once;
            return SaveClip(clip, toggleGroup.layerName);
        }

        // ====クリップを保存====
        private AnimationClip SaveClip(AnimationClip clip, string folderName)
        {
            string rootPath = _avatar != null ? _avatar.name : _animatorController.name;
            string directoryPath = $"{_savePath}/{rootPath}/{folderName}";
            if (!AssetDatabase.IsValidFolder($"{_savePath}/{rootPath}"))
            {
                AssetDatabase.CreateFolder(_savePath, rootPath);
            }
            if (!AssetDatabase.IsValidFolder(directoryPath))
            {
                AssetDatabase.CreateFolder($"{_savePath}/{rootPath}", folderName);
            }
            string clipPath = $"{directoryPath}/{clip.name}.anim";
            AssetDatabase.CreateAsset(clip, clipPath);
            return clip;
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = "";
            Transform transform = obj.transform;
            while (transform != null)
            {
                if (transform.parent != null)
                {
                    path = transform.name + (path == "" ? "" : "/") + path;
                }
                transform = transform.parent;
            }
            return path;
        }

        private void CreateExpressionMenus()
        {
            string rootMenuPath = $"{_savePath}/{_avatar.name}";
            if (!AssetDatabase.IsValidFolder(rootMenuPath))
            {
                AssetDatabase.CreateFolder(_savePath, _avatar.name);
            }

            VRCExpressionsMenu rootExpressionsMenu;
            if (_rootMenu != null)
            {
                // ユーザーが RootMenu を直接指定している場合はこちらを使う
                rootExpressionsMenu = _rootMenu;
            }
            else
            {
                // 従来通り RootMenuName を使用
                VRCExpressionsMenu existingMenu =
                    AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>($"{rootMenuPath}/{_rootMenuName}.asset");
                if (existingMenu != null)
                {
                    rootExpressionsMenu = existingMenu;
                    Debug.LogWarning($"Menu with name '{_rootMenuName}' already exists. Using existing asset.");
                }
                else
                {
                    rootExpressionsMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    rootExpressionsMenu.name = _rootMenuName;
                    AssetDatabase.CreateAsset(rootExpressionsMenu, $"{rootMenuPath}/{rootExpressionsMenu.name}.asset");
                }
            }

            // 各グループのメニューを作成
            string subMenuPath = $"{_savePath}/{_avatar.name}/SubMenu";
            if (!AssetDatabase.IsValidFolder(subMenuPath))
            {
                AssetDatabase.CreateFolder($"{_savePath}/{_avatar.name}", "SubMenu");
            }

            VRCExpressionsMenu previousGroupMenu = null;
            VRCExpressionsMenu previousRootSubMenu = null;
            string previousProcessLayerName = null;
            int groupPage = 0;
            foreach (var toggleGroup in _toggleGroups)
            {
                // オブジェクトの数を元にページ数を計算
                int numObjects = toggleGroup.objects.Count;
                int objectsPerPage = 7; // ページあたりの最大オブジェクト数
                int numObjectPages = Mathf.CeilToInt((float)numObjects / objectsPerPage);

                for (int objectPageNum = 0; objectPageNum < numObjectPages; objectPageNum++)
                {
                    VRCExpressionsMenu groupExpressionsMenu = null;
                    groupExpressionsMenu = CreateInstance<VRCExpressionsMenu>();
                    groupExpressionsMenu.name = $"{toggleGroup.layerName} ExpressionsMenu_Page{objectPageNum + 1}";
                    AssetDatabase.CreateAsset(groupExpressionsMenu, $"{subMenuPath}/{groupExpressionsMenu.name}.asset");

                    int startIndex_o = objectPageNum * objectsPerPage;
                    int endIndex_o = Mathf.Min((objectPageNum + 1) * objectsPerPage, numObjects);
                    for (int index_o = startIndex_o; index_o < endIndex_o; index_o++)
                    {
                        string paramName = toggleGroup.parameterNames[index_o];
                        string customName = toggleGroup.customNames[index_o];
                        Texture2D propicon = toggleGroup.propIcon[index_o];

                        // groupExpressionsMenuに同じ名前のコントロールが既に存在していないか確認
                        bool controlExists = groupExpressionsMenu.controls.Any(control => control.name == customName);
                        if (!controlExists)
                        {
                            VRCExpressionsMenu.Control control = new()
                            {
                                name = customName,
                                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                                value = 1f,
                                parameter = new VRCExpressionsMenu.Control.Parameter()
                                {
                                    name = paramName
                                },
                                icon = toggleGroup.propIcon != null ? propicon : null
                            };
                            groupExpressionsMenu.controls.Add(control);
                        }
                        else
                        {
                            Debug.LogWarning($"Control '{customName}' already exists in {groupExpressionsMenu.name}. Skipping duplicate.");
                        }
                    }
                    EditorUtility.SetDirty(groupExpressionsMenu);
                    AssetDatabase.SaveAssets();

                    if (previousGroupMenu != null && previousProcessLayerName == toggleGroup.layerName)
                    {
                        if (groupExpressionsMenu.controls.Count == 1)
                        {
                            foreach (var control in groupExpressionsMenu.controls)
                            {
                                previousGroupMenu.controls.Add(control);
                                AssetDatabase.DeleteAsset($"{subMenuPath}/{toggleGroup.layerName} ExpressionsMenu_Page{objectPageNum + 1}.asset");
                                Debug.Log($"Delete{subMenuPath}/{toggleGroup.layerName} ExpressionsMenu_Page{objectPageNum + 1}.asset");
                            }
                        }
                        else
                        if (!previousGroupMenu.controls.Any(control => control.name == "Next Page"))
                        {
                            VRCExpressionsMenu.Control subMenuControl = new()
                            {
                                name = "Next Page",
                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                subMenu = groupExpressionsMenu
                            };
                            previousGroupMenu.controls.Add(subMenuControl);
                        }
                        else
                        {
                            VRCExpressionsMenu.Control existingSubMenuControl = previousGroupMenu.controls
                                .FirstOrDefault(control => control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                                                            control.name == "Next Page");
                            existingSubMenuControl.subMenu = groupExpressionsMenu;
                            existingSubMenuControl.icon = toggleGroup.groupIcon != null ? toggleGroup.groupIcon : null;
                            Debug.LogWarning($"Next Page control already exists in {previousGroupMenu.name}. Skipping duplicate.");
                        }
                        EditorUtility.SetDirty(previousGroupMenu);
                        AssetDatabase.SaveAssets();
                    }
                    else
                    {
                        Texture2D groupIcon = toggleGroup.groupIcon;

                        // すでに同じ名前のサブメニューが存在するか確認
                        bool menuExists = rootExpressionsMenu.controls.Any(control => control.name == toggleGroup.layerName);

                        // 既存のサブメニューコントロールを検索する
                        VRCExpressionsMenu.Control existingSubMenuControl = rootExpressionsMenu.controls
                            .FirstOrDefault(control => control.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                                                        control.name == toggleGroup.layerName);

                        if (!menuExists)
                        {
                            if (rootExpressionsMenu.controls.Count < 7)
                            {
                                VRCExpressionsMenu.Control subMenuControl = new()
                                {
                                    name = toggleGroup.layerName,
                                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                    subMenu = groupExpressionsMenu,
                                    icon = toggleGroup.groupIcon != null ? groupIcon : null
                                };
                                rootExpressionsMenu.controls.Add(subMenuControl);
                            }
                            else
                            {
                                if (rootExpressionsMenu.controls.Count == 7 || previousRootSubMenu != null && previousRootSubMenu.controls.Count == 7)
                                {
                                    VRCExpressionsMenu rootSubMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                                    rootSubMenu.name = $"{rootExpressionsMenu.name}_Page{groupPage + 1}";
                                    AssetDatabase.CreateAsset(rootSubMenu, $"{rootMenuPath}/{rootSubMenu.name}.asset");

                                    int currentIndex = _toggleGroups.IndexOf(toggleGroup);
                                    bool isLastGroup = currentIndex == (_toggleGroups.Count - 1);

                                    if (!isLastGroup)
                                    {
                                        if (rootExpressionsMenu.controls.Count == 7)
                                        {
                                            VRCExpressionsMenu.Control subMenuControl_root = new()
                                            {
                                                name = "Next Page",
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = rootSubMenu
                                            };
                                            rootExpressionsMenu.controls.Add(subMenuControl_root);

                                            VRCExpressionsMenu.Control subMenuControl_rootSub = new()
                                            {
                                                name = toggleGroup.layerName,
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = groupExpressionsMenu,
                                                icon = toggleGroup.groupIcon != null ? groupIcon : null
                                            };
                                            rootSubMenu.controls.Add(subMenuControl_rootSub);

                                            groupPage++;
                                            Debug.Log("add page to rootexmenu");
                                        }
                                        else
                                        if (previousRootSubMenu != null && previousRootSubMenu.controls.Count == 7)
                                        {
                                            VRCExpressionsMenu.Control subMenuControl_previous = new()
                                            {
                                                name = "Next Page",
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = rootSubMenu
                                            };
                                            previousRootSubMenu.controls.Add(subMenuControl_previous);

                                            VRCExpressionsMenu.Control subMenuControl_rootSub = new()
                                            {
                                                name = toggleGroup.layerName,
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = groupExpressionsMenu,
                                                icon = toggleGroup.groupIcon != null ? groupIcon : null
                                            };
                                            rootSubMenu.controls.Add(subMenuControl_rootSub);

                                            groupPage++;
                                            Debug.Log("add page to previousrootexmenu");
                                        }
                                    }
                                    else
                                    {
                                        if (_toggleGroups.Count == 8 && rootExpressionsMenu.controls.Count == 7)
                                        {
                                            VRCExpressionsMenu.Control subMenuControl = new()
                                            {
                                                name = toggleGroup.layerName,
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = groupExpressionsMenu,
                                                icon = toggleGroup.groupIcon != null ? groupIcon : null
                                            };
                                            rootExpressionsMenu.controls.Add(subMenuControl);
                                            Debug.Log("add to root");
                                            AssetDatabase.DeleteAsset($"{rootMenuPath}/{rootExpressionsMenu.name}_Page{groupPage + 1}.asset");
                                            Debug.Log($"Delete{rootMenuPath}/{rootExpressionsMenu.name}_Page{groupPage + 1}.asset");
                                        }
                                        if (_toggleGroups.Count % 7 == 1 && _toggleGroups.Count > 14 && previousRootSubMenu != null && previousRootSubMenu.controls.Count == 7)
                                        {
                                            VRCExpressionsMenu.Control subMenuControl = new()
                                            {
                                                name = toggleGroup.layerName,
                                                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                                subMenu = groupExpressionsMenu,
                                                icon = toggleGroup.groupIcon != null ? groupIcon : null
                                            };
                                            previousRootSubMenu.controls.Add(subMenuControl);
                                            Debug.Log("add to sub root");
                                            AssetDatabase.DeleteAsset($"{rootMenuPath}/{rootExpressionsMenu.name}_Page{groupPage + 1}.asset");
                                            Debug.Log($"Delete{rootMenuPath}/{rootExpressionsMenu.name}_Page{groupPage + 1}.asset");
                                        }
                                    }
                                    previousRootSubMenu = rootSubMenu;
                                }
                                else
                                {
                                    VRCExpressionsMenu.Control subMenuControl = new()
                                    {
                                        name = toggleGroup.layerName,
                                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                                        subMenu = groupExpressionsMenu,
                                        icon = toggleGroup.groupIcon != null ? groupIcon : null
                                    };
                                    previousRootSubMenu.controls.Add(subMenuControl);
                                }

                            }

                            EditorUtility.SetDirty(rootExpressionsMenu);
                            AssetDatabase.SaveAssets();
                        }
                        else
                        {
                            // すでに存在する場合、そのメニューを使用
                            existingSubMenuControl.subMenu = groupExpressionsMenu;
                            existingSubMenuControl.icon = toggleGroup.groupIcon != null ? toggleGroup.groupIcon : null;
                            Debug.Log("既存のメニューを使用: " + toggleGroup.layerName);
                            Debug.Log($"{existingSubMenuControl.subMenu}");
                        }

                    }

                    // Update the previous group menu
                    previousGroupMenu = groupExpressionsMenu;
                    previousProcessLayerName = toggleGroup.layerName;
                }
            }

            EditorUtility.SetDirty(rootExpressionsMenu);
            AssetDatabase.SaveAssets();

            // ★ RootMenu が指定されている場合は _vrcExpressionsMenu への追加をスキップする
            if (_rootMenu != null)
            {
                return;
            }

            bool rootMenuAdded = false;
            VRCExpressionsMenu.Control existingControl = null;
            foreach (var control in _vrcExpressionsMenu.controls)
            {
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.name == _rootMenuName)
                {
                    rootMenuAdded = true;
                    existingControl = control;
                    break;
                }
            }

            if (!rootMenuAdded)
            {
                VRCExpressionsMenu.Control rootSubMenuControl = new()
                {
                    name = _rootMenuName,
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = rootExpressionsMenu
                };
                _vrcExpressionsMenu.controls.Insert(0, rootSubMenuControl);
            }
            // RootExpressionsMenuがすでに追加されている場合は、既存のコントロールを更新
            else
            {
                existingControl.subMenu = rootExpressionsMenu;
            }

            EditorUtility.SetDirty(_vrcExpressionsMenu);
            AssetDatabase.SaveAssets();
        }

        private void AddExpressionParameters()
        {
            // 既存のパラメーター名を保存
            List<string> existingParameters = _vrcExpressionParameters.parameters.Select(parameter => parameter.name).ToList();

            foreach (var toggleGroup in _toggleGroups)
            {
                for (int i = 0; i < toggleGroup.objects.Count; i++)
                {
                    // 変数から設定値を取得
                    string parameterName = toggleGroup.parameterNames[i];
                    bool defaultValue = toggleGroup.objects.Any(obj => obj.activeSelf && toggleGroup.parameterNames[toggleGroup.objects.IndexOf(obj)] == parameterName);
                    bool saved = i < toggleGroup.save.Count ? toggleGroup.save[i] : true;
                    bool synced = i < toggleGroup.sync.Count ? toggleGroup.sync[i] : true;

                    // parameterNamesが既存のexpression parametersに無い場合パラメーターを追加
                    if (!existingParameters.Contains(parameterName))
                    {
                        var length = _vrcExpressionParameters.parameters.Length;
                        Array.Resize(ref _vrcExpressionParameters.parameters, length + 1);
                        _vrcExpressionParameters.parameters[length] = new VRCExpressionParameters.Parameter() 
                        {   
                            name = parameterName,
                            valueType = VRCExpressionParameters.ValueType.Bool,
                            saved = saved,
                            networkSynced = synced,
                            defaultValue = defaultValue ? 1 : 0
                        };
                    }
                    else
                    {
                        // 既存のパラメータを更新
                        var parameterIndex = Array.FindIndex(_vrcExpressionParameters.parameters, p => p.name == parameterName);
                        _vrcExpressionParameters.parameters[parameterIndex].defaultValue = defaultValue ? 1 : 0;
                        _vrcExpressionParameters.parameters[parameterIndex].saved = saved;
                        _vrcExpressionParameters.parameters[parameterIndex].networkSynced = synced;
                    }
                }
            }
            // Avatar descriptorを更新
            EditorUtility.SetDirty(_avatar.expressionParameters);
        }

        // ====Save and Load====
        private void SaveToggleGroups()
        {
            string defaultPath = string.IsNullOrEmpty(_savePath) ? "Assets" : _savePath;
            string avatarName = _avatar != null ? _avatar.gameObject.name : "";
            string defaultFileName = string.IsNullOrEmpty(avatarName) ? "toggleGroups.json" : $"{avatarName}_toggleGroups.json";
            string path = EditorUtility.SaveFilePanel("Save Script Groups", defaultPath, defaultFileName, "json");
            if (string.IsNullOrEmpty(path))
                return;

            SaveData saveData = new SaveData
            {
                savePath = _savePath,
                rootMenuPath = _rootMenu != null ? AssetDatabase.GetAssetPath(_rootMenu) : string.Empty,
                rootMenuName = _rootMenuName,
                toggleGroups = _toggleGroups.Select(group => new SerializableToggleGroup(group)).ToList()
            };

            string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
            File.WriteAllText(path, json);

            AssetDatabase.Refresh();
        }

        private void LoadToggleGroups()
        {
            string defaultPath = string.IsNullOrEmpty(_savePath) ? "Assets" : _savePath;
            string path = EditorUtility.OpenFilePanel("Load Script Groups", defaultPath, "json");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                Debug.Log("Loaded JSON: " + json);

                SaveData saveData = JsonConvert.DeserializeObject<SaveData>(json);

                if (saveData == null)
                {
                    Debug.LogError("Failed to deserialize save data.");
                    return;
                }

                _savePath = saveData.savePath;
                _rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(saveData.rootMenuPath);
                _rootMenuName = saveData.rootMenuName;

                _toggleGroups.Clear();
                foreach (var serializableGroup in saveData.toggleGroups)
                {
                    if (serializableGroup == null)
                    {
                        Debug.LogError("One of the deserialized script groups is null.");
                        continue;
                    }
                    _toggleGroups.Add(serializableGroup.ToToggleGroup());
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load script groups: {ex.Message}");
            }

            Repaint();
        }

        [Serializable]
        public class SerializableToggleGroup
        {
            public string layerName;
            public string groupIconPath;
            public bool exclusiveMode;
            public bool allowDisableAll;
            public List<string> objectPaths;
            public bool isFoldout;
            public List<bool> isSettingsFoldout;
            public List<bool> save;
            public List<bool> sync;
            public List<string> propIconPaths;
            public List<string> customNames;
            public List<string> parameterNames;

            public SerializableToggleGroup(ToggleGroup group)
            {
                if (group == null)
                {
                    // 必ずnullなるが、問題ないのでreturn
                    return;
                }

                isFoldout = group.isFoldout;
                isSettingsFoldout = group.isSettingsFoldout != null ? new List<bool>(group.isSettingsFoldout) : new List<bool>();
                layerName = group.layerName;
                groupIconPath = group.groupIcon != null ? AssetDatabase.GetAssetPath(group.groupIcon) : string.Empty;
                exclusiveMode = group.exclusiveMode;
                allowDisableAll = group.allowDisableAll;
                objectPaths = new List<string>();
                if (group.objects != null)
                {
                    foreach (var obj in group.objects)
                    {
                        objectPaths.Add(obj != null ? GetGameObjectPath(obj) : string.Empty);
                    }
                }
                save = group.save != null ? new List<bool>(group.save) : new List<bool>();
                sync = group.sync != null ? new List<bool>(group.sync) : new List<bool>();
                propIconPaths = new List<string>();
                if (group.propIcon != null)
                {
                    foreach (var propIcon in group.propIcon)
                    {
                        propIconPaths.Add(propIcon != null ? AssetDatabase.GetAssetPath(propIcon) : string.Empty);
                    }
                }
                parameterNames = group.parameterNames != null ? new List<string>(group.parameterNames) : new List<string>();
                customNames = group.customNames != null ? new List<string>(group.customNames) : new List<string>();
            }

            public ToggleGroup ToToggleGroup()
            {
                ToggleGroup group = new ToggleGroup
                {
                    isFoldout = isFoldout,
                    isSettingsFoldout = new List<bool>(isSettingsFoldout),
                    layerName = layerName,
                    groupIcon = !string.IsNullOrEmpty(groupIconPath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(groupIconPath) : null,
                    exclusiveMode = exclusiveMode,
                    allowDisableAll = allowDisableAll,
                    objects = new List<GameObject>(),
                    save = new List<bool>(save),
                    sync = new List<bool>(sync),
                    propIcon = new List<Texture2D>(),
                    parameterNames = new List<string>(parameterNames),
                    customNames = new List<string>(customNames)
                };

                foreach (var path in objectPaths)
                {
                    GameObject obj = FindGameObjectByPath(path);
                    if (obj != null)
                    {
                        group.objects.Add(obj);
                    }
                }

                foreach (var path in propIconPaths)
                {
                    group.propIcon.Add(!string.IsNullOrEmpty(path) ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null);
                }

                return group;
            }

            private string GetGameObjectPath(GameObject obj)
            {
                if (obj == null) return string.Empty;
                string path = obj.name;
                Transform current = obj.transform;
                while (current.parent != null)
                {
                    current = current.parent;
                    path = current.name + "/" + path;
                }
                return path;
            }

            private GameObject FindGameObjectByPath(string path)
            {
                return string.IsNullOrEmpty(path) ? null : GameObject.Find(path);
            }
        }

        [Serializable]
        public class SaveData
        {
            public string savePath;
            public string rootMenuPath;
            public string rootMenuName;
            public List<SerializableToggleGroup> toggleGroups = new List<SerializableToggleGroup>();
        }
    }
}
