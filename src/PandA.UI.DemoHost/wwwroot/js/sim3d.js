let host;
let dotnet;
let animationId = 0;
let rendererMode = "loading";
let three;
let scene;
let camera;
let renderer;
let root;
let controls;
let defaultCamera;
let beltMesh;
let printerGroup;
let cartons = new Map();
let eyes = new Map();
let tampHead;
let fallbackCanvas;
let fallbackContext;

export async function start(element, dotnetReference) {
    host = element;
    dotnet = dotnetReference;
    host.innerHTML = "";

    try {
        three = await import("https://unpkg.com/three@0.160.0/build/three.module.js");
        const orbit = await import("https://unpkg.com/three@0.160.0/examples/jsm/controls/OrbitControls.js");
        import("https://cdn.jsdelivr.net/npm/@dimforge/rapier3d-compat@0.13.1/rapier.es.js").catch(() => undefined);
        initThree(orbit.OrbitControls);
        rendererMode = "three";
    } catch {
        initFallback();
        rendererMode = "fallback";
    }

    loop();
}

export function stop() {
    cancelAnimationFrame(animationId);
    animationId = 0;
    cartons.clear();
    eyes.clear();
    if (renderer) {
        renderer.dispose();
    }
    window.removeEventListener("resize", resize);
}

export function resetView() {
    setCameraPreset("perspective");
}

export function setCameraPreset(preset) {
    if (!camera || !controls) {
        return;
    }

    const target = new three.Vector3(78, 5, 0);
    if (preset === "top") {
        camera.position.set(80, 178, 0.1);
    } else if (preset === "side") {
        camera.position.set(80, 38, 154);
    } else {
        camera.position.copy(defaultCamera ?? new three.Vector3(34, 54, 118));
    }

    controls.target.copy(target);
    controls.update();
}

function initThree(OrbitControls) {
    scene = new three.Scene();
    scene.background = new three.Color(0x111827);

    const { width, height } = host.getBoundingClientRect();
    camera = new three.PerspectiveCamera(45, width / Math.max(height, 1), 0.1, 1200);
    camera.position.set(34, 54, 118);
    camera.lookAt(0, 0, 0);
    defaultCamera = camera.position.clone();

    renderer = new three.WebGLRenderer({ antialias: true, alpha: false });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.setSize(width, height);
    host.appendChild(renderer.domElement);

    controls = new OrbitControls(camera, renderer.domElement);
    controls.target.set(78, 5, 0);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.screenSpacePanning = true;
    controls.minDistance = 35;
    controls.maxDistance = 360;
    controls.update();

    scene.add(new three.HemisphereLight(0xdbeafe, 0x111827, 2.2));
    const key = new three.DirectionalLight(0xffffff, 2.4);
    key.position.set(-45, 70, 35);
    scene.add(key);

    root = new three.Group();
    scene.add(root);
    buildLine();
    window.addEventListener("resize", resize);
}

function buildLine() {
    beltMesh = new three.Mesh(
        new three.BoxGeometry(260, 2.8, 22),
        new three.MeshStandardMaterial({ color: 0x263244, roughness: 0.72, metalness: 0.12 })
    );
    beltMesh.position.set(80, 0, 0);
    root.add(beltMesh);

    const railMaterial = new three.MeshStandardMaterial({ color: 0x94a3b8, roughness: 0.48, metalness: 0.35 });
    for (const z of [-14, 14]) {
        const rail = new three.Mesh(new three.BoxGeometry(268, 1.4, 1.2), railMaterial);
        rail.position.set(80, 3.4, z);
        root.add(rail);
    }

    const rollerMaterial = new three.MeshStandardMaterial({ color: 0x64748b, roughness: 0.55, metalness: 0.4 });
    for (let x = -42; x <= 202; x += 12) {
        const roller = new three.Mesh(new three.CylinderGeometry(1.1, 1.1, 24, 18), rollerMaterial);
        roller.rotation.x = Math.PI / 2;
        roller.position.set(x, 2.2, 0);
        root.add(roller);
    }

    printerGroup = new three.Group();
    root.add(printerGroup);

    const floor = new three.Mesh(
        new three.PlaneGeometry(340, 110),
        new three.MeshStandardMaterial({ color: 0x0b1220, roughness: 0.9 })
    );
    floor.rotation.x = -Math.PI / 2;
    floor.position.y = -2;
    root.add(floor);
}

async function loop() {
    const snapshot = await dotnet.invokeMethodAsync("GetSnapshot");
    renderSnapshot(snapshot);
    animationId = requestAnimationFrame(loop);
}

function renderSnapshot(snapshot) {
    if (rendererMode === "fallback") {
        renderFallback(snapshot);
        return;
    }

    syncEyes(snapshot.eyes ?? []);
    syncPrinters(snapshot.settings ?? {});
    syncCartons(snapshot.cartons ?? []);
    if (tampHead) {
        const applying = (snapshot.cartons ?? []).some(c => c.state === "Applied" && Math.abs(c.positionInches - 120) < 16);
        tampHead.position.z = applying ? -7 : -13;
    }

    function syncPrinters(settings) {
        const count = Math.max(1, Math.min(settings.printerCount ?? 1, 4));
        const length = settings.conveyorLengthInches ?? 260;
        if (beltMesh) {
            beltMesh.scale.x = length / 260;
            beltMesh.position.x = toSceneX(length / 2);
        }

        while (printerGroup.children.length > 0) {
            printerGroup.remove(printerGroup.children[0]);
        }

        const printerMaterial = new three.MeshStandardMaterial({ color: 0x0f766e, roughness: 0.5, metalness: 0.15 });
        const tampMaterial = new three.MeshStandardMaterial({ color: 0xfacc15, roughness: 0.38, metalness: 0.1 });
        const start = length * 0.38;
        const spacing = 22;
        for (let i = 0; i < count; i++) {
            const x = toSceneX(start + i * spacing);
            const printer = new three.Mesh(new three.BoxGeometry(20, 28, 16), printerMaterial);
            printer.position.set(x, 16, -26);
            printerGroup.add(printer);

            const head = new three.Mesh(new three.BoxGeometry(14, 8, 3), tampMaterial);
            head.position.set(x + 10, 11, -13);
            printerGroup.add(head);
            if (i === 0) {
                tampHead = head;
            }
        }
    }
    controls?.update();
    renderer.render(scene, camera);
}

function syncEyes(nextEyes) {
    const seen = new Set();
    for (const eye of nextEyes) {
        seen.add(eye.id);
        let group = eyes.get(eye.id);
        if (!group) {
            group = makeEye(eye.id);
            eyes.set(eye.id, group);
            root.add(group);
        }
        group.position.x = toSceneX(eye.positionInches);
        const beam = group.getObjectByName("beam");
        beam.material.color.set(eye.active ? 0xf97316 : 0x38bdf8);
        beam.material.emissive.set(eye.active ? 0xf97316 : 0x0369a1);
    }
    for (const [id, group] of eyes) {
        if (!seen.has(id)) {
            root.remove(group);
            eyes.delete(id);
        }
    }
}

function makeEye(id) {
    const group = new three.Group();
    const postMaterial = new three.MeshStandardMaterial({ color: 0xcbd5e1, roughness: 0.45, metalness: 0.3 });
    const post = new three.Mesh(new three.CylinderGeometry(0.8, 0.8, 18, 12), postMaterial);
    post.position.set(0, 9, 16);
    group.add(post);
    const head = new three.Mesh(new three.BoxGeometry(5, 4, 4), postMaterial);
    head.position.set(0, 18, 13);
    group.add(head);
    const beam = new three.Mesh(
        new three.BoxGeometry(1.1, 0.45, 30),
        new three.MeshStandardMaterial({ color: 0x38bdf8, emissive: 0x0369a1, emissiveIntensity: 1.2 })
    );
    beam.name = "beam";
    beam.position.set(0, 17.5, 0);
    group.add(beam);
    return group;
}

function syncCartons(nextCartons) {
    const seen = new Set();
    for (const carton of nextCartons) {
        seen.add(carton.blindLabel);
        let group = cartons.get(carton.blindLabel);
        if (!group) {
            group = makeCarton(carton);
            cartons.set(carton.blindLabel, group);
            root.add(group);
        }
        group.position.x = toSceneX(carton.positionInches + carton.lengthInches / 2);
        group.userData.body.material.color.set(colorFor(carton.state));
        syncLabels(group, carton);
    }
    for (const [id, group] of cartons) {
        if (!seen.has(id)) {
            root.remove(group);
            cartons.delete(id);
        }
    }
}

function makeCarton(carton) {
    const group = new three.Group();
    const body = new three.Mesh(
        new three.BoxGeometry(carton.lengthInches, carton.heightInches, carton.widthInches),
        new three.MeshStandardMaterial({ color: colorFor(carton.state), roughness: 0.82 })
    );
    body.position.y = 3.2 + carton.heightInches / 2;
    group.add(body);
    group.userData.body = body;
    group.userData.labels = new Map();
    return group;
}

function syncLabels(group, carton) {
    const labels = carton.labels ?? [];
    const seen = new Set();
    for (const label of labels) {
        const key = `${label.labelType}-${label.lpn}`;
        seen.add(key);
        let mesh = group.userData.labels.get(key);
        if (!mesh) {
            mesh = new three.Mesh(
                new three.PlaneGeometry(7, 4),
                new three.MeshBasicMaterial({ color: 0xfef3c7, side: three.DoubleSide })
            );
            group.userData.labels.set(key, mesh);
            group.add(mesh);
        }
        const onTop = label.labelType === "Content";
        mesh.rotation.set(onTop ? -Math.PI / 2 : 0, 0, 0);
        mesh.position.set((label.cartonOffsetInches ?? carton.lengthInches / 2) - carton.lengthInches / 2, onTop ? 15.35 : 9.2, onTop ? 0 : 8.15);
        mesh.material.color.set(label.applied ? 0x86efac : 0xfef3c7);
    }
    for (const [key, mesh] of group.userData.labels) {
        if (!seen.has(key)) {
            group.remove(mesh);
            group.userData.labels.delete(key);
        }
    }
}

function colorFor(state) {
    if (state === "Verified") return 0x22c55e;
    if (state === "Rejected") return 0xef4444;
    if (state === "Printed" || state === "Applied") return 0x60a5fa;
    if (state === "Scanned") return 0xf59e0b;
    return 0xc08457;
}

function toSceneX(inches) {
    return inches - 50;
}

function resize() {
    if (!renderer || !camera || !host) return;
    const { width, height } = host.getBoundingClientRect();
    camera.aspect = width / Math.max(height, 1);
    camera.updateProjectionMatrix();
    renderer.setSize(width, height);
}

function initFallback() {
    fallbackCanvas = document.createElement("canvas");
    fallbackCanvas.dataset.renderer = "2d-fallback";
    host.appendChild(fallbackCanvas);
    fallbackContext = fallbackCanvas.getContext("2d");
    rendererMode = "fallback";
}

function renderFallback(snapshot) {
    const rect = host.getBoundingClientRect();
    fallbackCanvas.width = Math.max(1, rect.width);
    fallbackCanvas.height = Math.max(1, rect.height);
    const ctx = fallbackContext;
    ctx.fillStyle = "#111827";
    ctx.fillRect(0, 0, fallbackCanvas.width, fallbackCanvas.height);
    const y = fallbackCanvas.height * 0.55;
    ctx.fillStyle = "#263244";
    ctx.fillRect(40, y, fallbackCanvas.width - 80, 34);
    for (const eye of snapshot.eyes ?? []) {
        const x = 40 + (eye.positionInches / snapshot.conveyorLengthInches) * (fallbackCanvas.width - 80);
        ctx.fillStyle = eye.active ? "#f97316" : "#38bdf8";
        ctx.fillRect(x - 2, y - 70, 4, 104);
    }
    for (const carton of snapshot.cartons ?? []) {
        const x = 40 + (carton.positionInches / snapshot.conveyorLengthInches) * (fallbackCanvas.width - 80);
        ctx.fillStyle = carton.state === "Verified" ? "#22c55e" : carton.state === "Rejected" ? "#ef4444" : "#c08457";
        ctx.fillRect(x - 18, y - 28, 36, 28);
        ctx.fillStyle = "#fef3c7";
        ctx.fillRect(x - 4, y - 26, 14, 8);
    }
    ctx.fillStyle = "#e5e7eb";
    ctx.font = "14px system-ui";
    ctx.fillText(`Three.js CDN unavailable; rendering C# snapshot fallback. ${snapshot.lastEvent}`, 20, 28);
}
