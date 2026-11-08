using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KerbalDoom
{
    // Own view pose; stock KerbalEVA exclusively owns locomotion, foot correction and Rigidbody rotation.
    public sealed class CameraBridge
    {
        private Vessel owner;
        private KerbalEVA eva;
        private Transform oldParent;
        private Vector3 oldLocalPosition;
        private Quaternion oldLocalRotation;
        private Vector3 heading;
        private float pitch, yaw, walk, run, strafe, oldFoV;
        private bool characterMode, characterToggle;
        private bool movementSaved, rigSaved;
        private KeyCodeExtended originalPackPrimary, originalPackSecondary, blockedPack;
        private readonly Dictionary<Renderer, bool> renderers = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Camera, float> nearClips = new Dictionary<Camera, float>();
        private readonly Dictionary<Behaviour, bool> competing = new Dictionary<Behaviour, bool>();
        private CameraManager.CameraMode originalMode;
        private InternalCamera internalController;
        private Camera internalViewCamera;
        private Kerbal internalKerbal;
        private Transform internalParent;
        private Vector3 internalLocalPosition;
        private Quaternion internalLocalRotation;
        private bool internalScriptEnabled, internalSaved, createdIva;
        public float Sensitivity = 2, Speed = 1, Recoil;
        public bool IsFirstPerson { get { return owner != null; } }
        public bool Cockpit { get { return owner != null && !owner.isEVA; } }
        public Camera ViewCamera { get { return internalViewCamera != null ? internalViewCamera : FlightCamera.fetch == null ? null : FlightCamera.fetch.mainCamera; } }
        // KSP IVA uses a separate internal coordinate space. Never use ViewCamera.transform for world raycasts.
        public Transform AimTransform { get { return FlightCamera.fetch == null || FlightCamera.fetch.mainCamera == null ? null : FlightCamera.fetch.mainCamera.transform; } }
        public bool Connect() { return FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null; }

        public void Enter(Vessel vessel)
        {
            if (vessel == null || !Connect()) throw new InvalidOperationException("Flight camera is unavailable");
            owner = vessel; eva = vessel.isEVA ? vessel.evaController : null;
            if (vessel.isEVA && eva == null) throw new InvalidOperationException("EVA controller unavailable");
            if (eva != null)
            {
                walk = eva.walkSpeed; run = eva.runSpeed; strafe = eva.strafeSpeed;
                characterMode = eva.CharacterFrameMode; characterToggle = eva.CharacterFrameModeToggle; movementSaved = true;
            }
            SuspendCompetingCamera(vessel);
            Transform rig = FlightCamera.fetch.transform;
            oldParent = rig.parent; oldLocalPosition = rig.localPosition; oldLocalRotation = rig.localRotation;
            oldFoV = FlightCamera.fetch.FieldOfView; rigSaved = true;
            CameraManager manager = CameraManager.Instance;
            originalMode = manager == null ? CameraManager.CameraMode.Flight : manager.currentCameraMode;
            if (Cockpit) SetupCockpit(manager);
            else
            {
                // R belongs to the stock jetpack toggle; reserve it only for reload during EVA combat.
                originalPackPrimary = GameSettings.EVA_TogglePack.primary; originalPackSecondary = GameSettings.EVA_TogglePack.secondary;
                blockedPack = new KeyCodeExtended(KeyCode.None);
                GameSettings.EVA_TogglePack.primary = blockedPack; GameSettings.EVA_TogglePack.secondary = blockedPack;
            }
            Vector3 up = Up();
            heading = SafeTangent(vessel.transform.forward, up);
            if (heading.sqrMagnitude < .1f) heading = SafeTangent(rig.forward, up);
            Camera camera = FlightCamera.fetch.mainCamera;
            if (camera != null) { nearClips[camera] = camera.nearClipPlane; camera.nearClipPlane = .03f; }
            FlightCamera.fetch.DeactivateUpdate(); rig.SetParent(null, true);
            Move(); Ensure(vessel);
        }
        private void SetupCockpit(CameraManager manager)
        {
            if (manager == null) return;
            if (manager.currentCameraMode != CameraManager.CameraMode.IVA && manager.currentCameraMode != CameraManager.CameraMode.Internal)
            {
                // A crewed ship can enter an actual IVA; probes retain a forward first-person camera instead.
                createdIva = manager.SetCameraIVA();
            }
            if (manager.currentCameraMode != CameraManager.CameraMode.IVA && manager.currentCameraMode != CameraManager.CameraMode.Internal) return;
            internalController = InternalCamera.Instance;
            if (internalController == null) return;
            internalViewCamera = internalController.GetComponent<Camera>();
            if (internalViewCamera == null || !internalViewCamera.enabled) { internalViewCamera = null; return; }
            Transform rig = internalController.transform;
            internalParent = rig.parent; internalLocalPosition = rig.localPosition; internalLocalRotation = rig.localRotation;
            internalScriptEnabled = internalController.enabled; internalSaved = true;
            internalKerbal = manager.IVACameraActiveKerbal;
            internalController.enabled = false;
        }
        private void SuspendCompetingCamera(Vessel vessel)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                foreach (string name in new [] { "FirstPerson.FirstPersonEVA", "FirstPerson.ThroughTheEyes" })
                {
                    Type type = assembly.GetType(name, false);
                    if (type == null) continue;
                    foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type))
                    {
                        Behaviour behaviour = obj as Behaviour;
                        if (behaviour == null) continue;
                        // Release an existing third-party EVA view, but never call it for our camera pose.
                        if (eva != null)
                        {
                            try
                            {
                                FieldInfo field = type.GetField("fpCameraManager");
                                object manager = field == null ? null : field.GetValue(obj);
                                FieldInfo active = manager == null ? null : manager.GetType().GetField("isFirstPerson");
                                MethodInfo reset = manager == null ? null : manager.GetType().GetMethod("resetCamera");
                                if (active != null && reset != null && (bool)active.GetValue(manager)) reset.Invoke(manager, new object[] { vessel });
                            }
                            catch (Exception e) { Debug.LogWarning("[KerbalDoom] Optional camera release: " + e.Message); }
                        }
                        competing[behaviour] = behaviour.enabled; behaviour.enabled = false;
                    }
                }
        }
        public void Exit(Vessel vessel)
        {
            if (blockedPack != null)
            {
                if (object.ReferenceEquals(GameSettings.EVA_TogglePack.primary, blockedPack)) GameSettings.EVA_TogglePack.primary = originalPackPrimary;
                if (object.ReferenceEquals(GameSettings.EVA_TogglePack.secondary, blockedPack)) GameSettings.EVA_TogglePack.secondary = originalPackSecondary;
            }
            foreach (var pair in renderers) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            foreach (var pair in nearClips) if (pair.Key != null) pair.Key.nearClipPlane = pair.Value;
            if (eva != null && movementSaved)
            {
                eva.walkSpeed = walk; eva.runSpeed = run; eva.strafeSpeed = strafe;
                eva.CharacterFrameMode = characterMode; eva.CharacterFrameModeToggle = characterToggle;
            }
            if (internalController != null && internalSaved)
            {
                internalController.transform.SetParent(internalParent, false);
                internalController.transform.localPosition = internalLocalPosition;
                internalController.transform.localRotation = internalLocalRotation;
                internalController.enabled = internalScriptEnabled;
            }
            CameraManager manager = CameraManager.Instance;
            bool retainIva = internalSaved && !createdIva && FlightGlobals.ActiveVessel == owner;
            if ((createdIva || internalSaved && !retainIva) && manager != null) manager.SetCameraFlight();
            if (FlightCamera.fetch != null && rigSaved)
            {
                Transform rig = FlightCamera.fetch.transform;
                rig.SetParent(oldParent != null ? oldParent : FlightCamera.fetch.GetPivot(), false);
                rig.localPosition = oldLocalPosition; rig.localRotation = oldLocalRotation;
                FlightCamera.fetch.SetFoV(oldFoV);
                if (retainIva) FlightCamera.fetch.DeactivateUpdate();
                else FlightCamera.fetch.ActivateUpdate();
            }
            foreach (var pair in competing) if (pair.Key != null) pair.Key.enabled = pair.Value;
            owner = null; eva = null; internalController = null; internalViewCamera = null; internalKerbal = null;
            blockedPack = null; movementSaved = false; rigSaved = false; internalSaved = false; createdIva = false;
            pitch = 0; yaw = 0; renderers.Clear(); nearClips.Clear(); competing.Clear();
        }
        public void Look(float dt, bool input)
        {
            if (owner == null) return;
            if (!Cockpit) heading = SafeTangent(heading, Up());
            if (input)
            {
                float dx = Input.GetAxis("Mouse X") * Sensitivity;
                if (Cockpit) yaw = Mathf.Repeat(yaw + dx + 180, 360) - 180;
                else heading = Quaternion.AngleAxis(dx, Up()) * heading;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * Sensitivity, -85, 85);
            }
            Recoil = Mathf.MoveTowards(Recoil, 0, dt * 12);
        }
        public void Move()
        {
            if (eva == null) return;
            eva.walkSpeed = 4.5f * Mathf.Clamp(Speed, .5f, 3); eva.strafeSpeed = eva.walkSpeed; eva.runSpeed = 8.5f * Mathf.Clamp(Speed, .5f, 3);
            // Native FixedUpdate obtains fFwd from our view, HandleMovementInput sets tgtFwd, then
            // UpdateHeading + correctGroundedRotation own Rigidbody rotation and the foot pivot.
            // Never MoveRotation/teleport/replace velocity here: that caused the running-in-place bug.
            eva.CharacterFrameMode = true; eva.CharacterFrameModeToggle = true;
        }
        public void Ensure(Vessel vessel)
        {
            if (owner == null || FlightCamera.fetch == null) return;
            if (eva != null) Hide(owner.GetComponentsInChildren<Renderer>(true));
            else if (internalKerbal != null) Hide(internalKerbal.GetComponentsInChildren<Renderer>(true));
            FlightCamera.fetch.DeactivateUpdate();
            Transform rig = FlightCamera.fetch.transform;
            rig.SetParent(null, true);
            if (Cockpit)
            {
                Quaternion look = Quaternion.Euler(pitch - Recoil, yaw, 0);
                if (internalController != null && internalSaved && internalParent != null)
                {
                    internalController.enabled = false;
                    Transform internalRig = internalController.transform;
                    internalRig.position = internalParent.TransformPoint(internalLocalPosition);
                    internalRig.rotation = internalParent.rotation * internalLocalRotation * look;
                    // Same conversion used by native InternalCamera.Update: world rays must follow this pose.
                    rig.position = InternalSpace.InternalToWorld(internalRig.position);
                    rig.rotation = InternalSpace.InternalToWorld(internalRig.rotation);
                    if (internalViewCamera != null) FlightCamera.fetch.SetFoV(internalViewCamera.fieldOfView);
                }
                else
                {
                    Transform reference = owner.ReferenceTransform != null ? owner.ReferenceTransform : owner.transform;
                    rig.position = reference.position + reference.up * .4f;
                    rig.rotation = Quaternion.LookRotation(reference.up, -reference.forward) * look;
                }
            }
            else
            {
                Vector3 up = Up();
                rig.position = owner.transform.position + up * .72f + heading * .08f;
                rig.rotation = Quaternion.LookRotation(heading, up) * Quaternion.Euler(pitch - Recoil, 0, 0);
            }
        }
        private void Hide(Renderer[] values)
        {
            foreach (Renderer renderer in values)
            {
                if (!renderers.ContainsKey(renderer)) renderers[renderer] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = true;
            }
        }
        private Vector3 Up() { return ((Vector3d)owner.transform.position - owner.mainBody.position).normalized; }
        private static Vector3 SafeTangent(Vector3 direction, Vector3 up)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(direction, up).normalized;
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.Cross(up, Vector3.right).normalized;
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.Cross(up, Vector3.forward).normalized;
            return tangent;
        }
    }
}
