using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class QuantumChildrenProximityManager : MonoBehaviour
{
    [System.Serializable]
    private sealed class SpawnedEncounter
    {
        public GameObject Instance;
        public PlayableGraph Graph;
        public AnimationClipPlayable Playable;
    }

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform originalModel;
    [SerializeField] private Transform encounterModel;
    [SerializeField] private Transform[] hiddenModels = System.Array.Empty<Transform>();

    [Header("Proximity")]
    [SerializeField] private float activationRange = 5f;
    [SerializeField] private int spawnCount = 4;
    [SerializeField] private float spawnRadius = 2.2f;

    [Header("Embedded Animation")]
    [SerializeField] private string encounterClipName;
    [SerializeField] private AnimationClip encounterClip;

    private bool isPlayerInRange;
    private readonly System.Collections.Generic.List<SpawnedEncounter> spawnedEncounters = new();

    private void Awake()
    {
        ResolvePlayer();
        ResolveEncounterClip();
        ApplyFarState();
    }

    private void OnDisable()
    {
        DestroySpawnedEncounters();
    }

    private void Update()
    {
        if (player == null)
        {
            ResolvePlayer();
            if (player == null)
            {
                return;
            }
        }

        Vector3 offsetToPlayer = player.position - GetEncounterAnchorPosition();
        offsetToPlayer.y = 0f;
        bool shouldBeInRange = offsetToPlayer.sqrMagnitude <= activationRange * activationRange;
        if (shouldBeInRange != isPlayerInRange)
        {
            isPlayerInRange = shouldBeInRange;

            if (isPlayerInRange)
            {
                ApplyNearState();
            }
            else
            {
                ApplyFarState();
            }
        }

        if (isPlayerInRange)
        {
            UpdateEncounterPlayback();
        }
    }

    private void OnValidate()
    {
        activationRange = Mathf.Max(0f, activationRange);
        spawnCount = Mathf.Max(1, spawnCount);
        spawnRadius = Mathf.Max(0.25f, spawnRadius);
#if UNITY_EDITOR
        ResolveEncounterClip();
#endif
    }

    private void ApplyNearState()
    {
        SetModelActive(originalModel, false);
        SetModelActive(encounterModel, false);
        SetHiddenModelsActive(false);
        SpawnEncounterGroup();
    }

    private void ApplyFarState()
    {
        DestroySpawnedEncounters();
        SetModelActive(originalModel, true);
        SetModelActive(encounterModel, false);
        SetHiddenModelsActive(false);
    }

    private void ResolveEncounterClip()
    {
#if UNITY_EDITOR
        if (encounterModel == null || string.IsNullOrWhiteSpace(encounterClipName))
        {
            return;
        }

        GameObject sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(encounterModel.gameObject);
        if (sourceObject == null)
        {
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(sourceObject);
        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is AnimationClip clip
                && !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal)
                && string.Equals(clip.name, encounterClipName, System.StringComparison.Ordinal))
            {
                encounterClip = clip;
                EditorUtility.SetDirty(this);
                return;
            }
        }
#endif
    }

    private void SpawnEncounterGroup()
    {
        if (encounterModel == null)
        {
            return;
        }

        DestroySpawnedEncounters();

        Vector3 playerPosition = player != null ? player.position : GetEncounterAnchorPosition();
        float angleOffset = Random.Range(0f, 360f);
        float yPosition = GetEncounterAnchorPosition().y;
        Vector3 spawnScale = encounterModel.lossyScale;

        for (int i = 0; i < spawnCount; i++)
        {
            float angle = angleOffset + (360f * i / spawnCount);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * spawnRadius;
            Vector3 spawnPosition = new Vector3(playerPosition.x + offset.x, yPosition, playerPosition.z + offset.z);

            GameObject instance = Instantiate(encounterModel.gameObject, spawnPosition, encounterModel.rotation);
            instance.name = $"{encounterModel.name}_Spawned_{i + 1}";
            instance.SetActive(true);
            instance.transform.localScale = spawnScale;
            AlignInstanceTowardPlayer(instance.transform, playerPosition);
            RandomizeSpotlights(instance.transform);

            SpawnedEncounter spawnedEncounter = new SpawnedEncounter
            {
                Instance = instance
            };

            StartEncounterPlayback(spawnedEncounter, i * 0.37d);
            spawnedEncounters.Add(spawnedEncounter);
        }
    }

    private void ResolvePlayer()
    {
        if (player != null)
        {
            return;
        }

        FirstPersonController controller = Object.FindFirstObjectByType<FirstPersonController>();
        if (controller != null)
        {
            player = controller.transform;
            return;
        }

        GameObject playerObject = GameObject.Find("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }

    private void StartEncounterPlayback(SpawnedEncounter spawnedEncounter, double timeOffset)
    {
        if (spawnedEncounter == null || spawnedEncounter.Instance == null || encounterClip == null)
        {
            return;
        }

        Animator animator = spawnedEncounter.Instance.GetComponent<Animator>();
        if (animator == null)
        {
            animator = spawnedEncounter.Instance.GetComponentInChildren<Animator>(true);
        }

        if (animator == null)
        {
            return;
        }

        spawnedEncounter.Graph = PlayableGraph.Create($"{name}_{encounterClipName}_{spawnedEncounter.Instance.GetInstanceID()}");
        spawnedEncounter.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(spawnedEncounter.Graph, "EncounterAnimation", animator);
        spawnedEncounter.Playable = AnimationClipPlayable.Create(spawnedEncounter.Graph, encounterClip);
        spawnedEncounter.Playable.SetApplyFootIK(false);
        spawnedEncounter.Playable.SetApplyPlayableIK(false);
        spawnedEncounter.Playable.SetTime(timeOffset);
        output.SetSourcePlayable(spawnedEncounter.Playable);
        spawnedEncounter.Graph.Play();
    }

    private void UpdateEncounterPlayback()
    {
        if (encounterClip == null || encounterClip.length <= 0f)
        {
            return;
        }

        double loopTime = Time.timeAsDouble % encounterClip.length;
        for (int i = 0; i < spawnedEncounters.Count; i++)
        {
            SpawnedEncounter spawnedEncounter = spawnedEncounters[i];
            if (spawnedEncounter == null || !spawnedEncounter.Graph.IsValid())
            {
                continue;
            }

            spawnedEncounter.Playable.SetTime(loopTime + (i * 0.37d));
            spawnedEncounter.Graph.Evaluate(0f);
        }
    }

    private void DestroySpawnedEncounters()
    {
        for (int i = 0; i < spawnedEncounters.Count; i++)
        {
            SpawnedEncounter spawnedEncounter = spawnedEncounters[i];
            if (spawnedEncounter == null)
            {
                continue;
            }

            if (spawnedEncounter.Graph.IsValid())
            {
                spawnedEncounter.Graph.Destroy();
            }

            if (spawnedEncounter.Instance != null)
            {
                Destroy(spawnedEncounter.Instance);
            }
        }

        spawnedEncounters.Clear();
    }

    private void SetHiddenModelsActive(bool isActive)
    {
        for (int i = 0; i < hiddenModels.Length; i++)
        {
            SetModelActive(hiddenModels[i], isActive);
        }
    }

    private Vector3 GetEncounterAnchorPosition()
    {
        if (originalModel != null)
        {
            return originalModel.position;
        }

        if (encounterModel != null)
        {
            return encounterModel.position;
        }

        return transform.position;
    }

    private static void SetModelActive(Transform modelRoot, bool isActive)
    {
        if (modelRoot != null)
        {
            modelRoot.gameObject.SetActive(isActive);
        }
    }

    private static void AlignInstanceTowardPlayer(Transform instance, Vector3 playerPosition)
    {
        Vector3 lookDirection = playerPosition - instance.position;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            instance.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }
    }

    private static void RandomizeSpotlights(Transform root)
    {
        if (root == null)
        {
            return;
        }

        Light[] lights = root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type != LightType.Spot)
            {
                continue;
            }

            lights[i].color = Random.ColorHSV(0f, 1f, 0.65f, 1f, 0.65f, 1f);
        }
    }
}
