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
let printers = new Map();
let cartons = new Map();
let eyes = new Map();
let fallbackCanvas;
let fallbackContext;

const REST_TAMP_Y = 42;   // top arm parked high above the belt
const REST_SIDE_Z = 30;   // side arm parked out toward its printer body
const ARM_HALF = 10;      // half-length of the tamp arm mesh
const BELT_TOP_Y = 3.2;   // top surface of the belt (carton sits here)

export async function start(element, dotnetReference) {
    host = element;
    dotnet = dotnetReference;
    host.innerHTML = "";

    try {
        three = await import("./three.module.js");
        const orbit = await import("./OrbitControls.js");
        initThree(orbit.OrbitControls);
        rendererMode = "three";
    } catch (err) {
        console.error("Three.js failed to initialise, using 2D fallback", err);
        initFallback();
        rendererMode = "fallback";
    }

    loop();
    return rendererMode;
}

export function stop() {
    cancelAnimationFrame(animationId);
    animationId = 0;
    cartons.clear();
    eyes.clear();
    printers.clear();
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

    const target = new three.Vector3(78, 6, 0);
    if (preset === "top") {
        camera.position.set(80, 178, 0.1);
    } else if (preset === "side") {
        camera.position.set(80, 30, 150);
    } else {
        camera.position.copy(defaultCamera ?? new three.Vector3(20, 46, 120));
    }

    controls.target.copy(target);
    controls.update();
}

function initThree(OrbitControls) {
    scene = new three.Scene();
    scene.background = new three.Color(0x0f1523);

    const { width, height } = host.getBoundingClientRect();
    camera = new three.PerspectiveCamera(45, width / Math.max(height, 1), 0.1, 1600);
    camera.position.set(20, 46, 120);
    camera.lookAt(78, 6, 0);
    defaultCamera = camera.position.clone();

    renderer = new three.WebGLRenderer({ antialias: true, alpha: false });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.setSize(width, height);
    host.appendChild(renderer.domElement);

    controls = new OrbitControls(camera, renderer.domElement);
    controls.target.set(78, 6, 0);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.screenSpacePanning = true;
    controls.minDistance = 30;
    controls.maxDistance = 420;
    controls.update();

    scene.add(new three.HemisphereLight(0xdbeafe, 0x0b1220, 2.0));
    const key = new three.DirectionalLight(0xffffff, 2.2);
    key.position.set(-45, 80, 45);
    scene.add(key);
    const fill = new three.DirectionalLight(0x93c5fd, 0.8);
    fill.position.set(60, 30, -60);
    scene.add(fill);

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

    const floor = new three.Mesh(
        new three.PlaneGeometry(360, 120),
        new three.MeshStandardMaterial({ color: 0x0b1220, roughness: 0.9 })
    );
    floor.rotation.x = -Math.PI / 2;
    floor.position.y = -2;
    root.add(floor);
}

// Build a print-and-apply station. A TOP station has an overhead gantry with a tamp arm that
// extends DOWN onto the carton top; a SIDE station has a horizontal arm that extends IN toward
// the carton's near (+z) face. Each station is placed statically at its printer-eye X.
function buildStation(printer) {
    const top = String(printer.orientation).toLowerCase() === "top";
    const group = new three.Group();

    const bodyMat = new three.MeshStandardMaterial({ color: top ? 0x0f766e : 0x9333ea, roughness: 0.5, metalness: 0.2 });
    const frameMat = new three.MeshStandardMaterial({ color: 0x334155, roughness: 0.6, metalness: 0.4 });
    const tampMat = new three.MeshStandardMaterial({ color: 0xf59e0b, roughness: 0.35, metalness: 0.2 });
    const slotMat = new three.MeshStandardMaterial({ color: 0xfef3c7 });

    const tamp = new three.Group();

    if (top) {
        // Body beside the belt at -z, gantry over the belt centre, vertical tamp arm.
        const body = new three.Mesh(new three.BoxGeometry(22, 26, 14), bodyMat);
        body.position.set(0, 16, -24);
        group.add(body);
        const slot = new three.Mesh(new three.BoxGeometry(10, 2, 1), slotMat);
        slot.position.set(0, 12, -16.6);
        group.add(slot);
        const gantry = new three.Mesh(new three.BoxGeometry(3.5, 3.5, 34), frameMat);
        gantry.position.set(0, 34, -8);
        group.add(gantry);
        const column = new three.Mesh(new three.BoxGeometry(4, 40, 4), frameMat);
        column.position.set(0, 20, -24);
        group.add(column);

        const arm = new three.Mesh(new three.BoxGeometry(3, ARM_HALF * 2, 3), frameMat);
        tamp.add(arm);
        const pad = new three.Mesh(new three.BoxGeometry(9, 1.4, 6), tampMat);
        pad.position.set(0, -ARM_HALF, 0);
        tamp.add(pad);
        tamp.position.set(0, REST_TAMP_Y, 0);
    } else {
        // Body beside the belt at +z (camera-facing), horizontal arm extending in -z onto the side face.
        const body = new three.Mesh(new three.BoxGeometry(22, 24, 14), bodyMat);
        body.position.set(0, 14, 26);
        group.add(body);
        const slot = new three.Mesh(new three.BoxGeometry(10, 2, 1), slotMat);
        slot.position.set(0, 12, 18.6);
        group.add(slot);
        const column = new three.Mesh(new three.BoxGeometry(4, 30, 4), frameMat);
        column.position.set(0, 15, 26);
        group.add(column);

        const arm = new three.Mesh(new three.BoxGeometry(3, 3, ARM_HALF * 2), frameMat);
        tamp.add(arm);
        const pad = new three.Mesh(new three.BoxGeometry(6, 9, 1.4), tampMat);
        pad.position.set(0, 0, -ARM_HALF);
        tamp.add(pad);
        tamp.position.set(0, BELT_TOP_Y + 8, REST_SIDE_Z);
    }

    group.add(tamp);
    group.userData.tamp = tamp;
    group.userData.top = top;
    return group;
}

// Create/update/remove the printer stations to match the snapshot, positioned statically at X.
function syncPrinters(printerList, cartonList) {
    const seen = new Set();
    for (const printer of printerList ?? []) {
        seen.add(printer.printerId);
        let group = printers.get(printer.printerId);
        if (!group) {
            group = buildStation(printer);
            printers.set(printer.printerId, group);
            root.add(group);
        }
        group.position.x = toSceneX(printer.positionInches);
        animateStation(group, cartonList);
    }
    for (const [id, group] of printers) {
        if (!seen.has(id)) {
            root.remove(group);
            printers.delete(id);
        }
    }
}

// Extend a station's tamp onto the carton when one is beneath it and being applied; retract otherwise.
function animateStation(group, cartonList) {
    const tamp = group.userData.tamp;
    if (!tamp) {
        return;
    }

    const top = group.userData.top;
    let under = null;
    for (const carton of cartonList ?? []) {
        const centerX = toSceneX(carton.positionInches + carton.lengthInches / 2);
        const applying = carton.state === "Applied" || carton.state === "Printed";
        if (applying && Math.abs(centerX - group.position.x) < carton.lengthInches / 2 + 3) {
            under = carton;
            break;
        }
    }

    if (top) {
        const targetY = under ? BELT_TOP_Y + under.heightInches + ARM_HALF + 0.6 : REST_TAMP_Y;
        tamp.position.y += (targetY - tamp.position.y) * 0.25;
    } else {
        const targetZ = under ? under.widthInches / 2 + ARM_HALF + 0.6 : REST_SIDE_Z;
        const targetY = under ? BELT_TOP_Y + under.heightInches / 2 : BELT_TOP_Y + 8;
        tamp.position.z += (targetZ - tamp.position.z) * 0.25;
        tamp.position.y += (targetY - tamp.position.y) * 0.25;
    }
}

async function loop() {
    try {
        const snapshot = await dotnet.invokeMethodAsync("GetSnapshot");
        renderSnapshot(snapshot);
    } catch {
        // circuit not ready yet; try again next frame
    }
    animationId = requestAnimationFrame(loop);
}

function renderSnapshot(snapshot) {
    if (rendererMode === "fallback") {
        renderFallback(snapshot);
        return;
    }

    const settings = snapshot.settings ?? {};
    const eyeList = snapshot.eyes ?? [];
    const cartonList = snapshot.cartons ?? [];
    const printerList = snapshot.printers ?? [];

    resizeBelt(settings);
    syncPrinters(printerList, cartonList);
    syncEyes(eyeList);
    syncCartons(cartonList);

    controls?.update();
    renderer.render(scene, camera);
}

function resizeBelt(settings) {
    const length = settings.conveyorLengthInches ?? 260;
    if (beltMesh) {
        beltMesh.scale.x = length / 260;
        beltMesh.position.x = toSceneX(length / 2);
    }
}

// Park the printer at the first "Printer Eye" (the apply point the tamp guards).
function positionPrinter(eyeList, cartonList) {
    const printerEye = eyeList.find(e => /printer/i.test(e.id)) ?? eyeList[Math.min(1, eyeList.length - 1)];
    // If a carton is carrying an apply point, prefer its real apply X so the tamp lines up
    // with the actual fire point being verified.
    const applyX = cartonList
        .flatMap(c => c.labels ?? [])
        .map(l => l.applyPoint?.x)
        .find(x => typeof x === "number");
    const inches = applyX ?? printerEye?.positionInches ?? 100;
    applicatorSceneX = toSceneX(inches);
    if (printerAssembly) {
        printerAssembly.position.x = applicatorSceneX;
    }
}

// Extend the tamp arm down when a carton is beneath the applicator, retract otherwise.
function animateTamp(cartonList) {
    if (!tampArm) {
        return;
    }

    let targetY = REST_TAMP_Y;
    for (const carton of cartonList) {
        const centerX = toSceneX(carton.positionInches + carton.lengthInches / 2);
        const underHead = Math.abs(centerX - applicatorSceneX) < carton.lengthInches / 2 + 3;
        const applying = carton.state === "Applied" || carton.state === "Printed";
        if (underHead && applying) {
            const boxTop = BELT_TOP_Y + carton.heightInches;
            targetY = boxTop + ARM_HALF + 0.6; // pad tip just touches the carton top
            break;
        }
    }

    // Smoothly approach the target so the stamp reads as a deliberate motion.
    tampArm.position.y += (targetY - tampArm.position.y) * 0.25;
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

function makeEye() {
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
    body.position.y = BELT_TOP_Y + carton.heightInches / 2;
    group.add(body);
    group.userData.body = body;
    group.userData.labels = new Map();
    return group;
}

// Only render a label once it has been APPLIED — before that the carton is bare, so the
// operator watches the tamp physically deposit the label at the fire point.
function syncLabels(group, carton) {
    const labels = (carton.labels ?? []).filter(l => l.applied);
    const seen = new Set();
    for (const label of labels) {
        const key = `${label.labelType}-${label.lpn}`;
        seen.add(key);
        let mesh = group.userData.labels.get(key);
        if (!mesh) {
            mesh = new three.Mesh(
                new three.PlaneGeometry(7, 4.5),
                new three.MeshBasicMaterial({ color: 0xf8fafc, side: three.DoubleSide })
            );
            group.userData.labels.set(key, mesh);
            group.add(mesh);
        }
        const onTop = String(label.orientation).toLowerCase() === "top";
        // cartonOffsetInches encodes the real fire point (leading/trailing/middle) along the box.
        // Clamp so the label plane always stays fully on the carton face rather than hanging off.
        const halfLabel = 3.5;
        const rawOffset = (label.cartonOffsetInches ?? carton.lengthInches / 2) - carton.lengthInches / 2;
        const limit = Math.max(0, carton.lengthInches / 2 - halfLabel);
        const offsetX = Math.max(-limit, Math.min(limit, rawOffset));
        if (onTop) {
            mesh.rotation.set(-Math.PI / 2, 0, 0);
            mesh.position.set(offsetX, BELT_TOP_Y + carton.heightInches + 0.06, 0);
        } else {
            mesh.rotation.set(0, 0, 0);
            mesh.position.set(offsetX, BELT_TOP_Y + carton.heightInches / 2, carton.widthInches / 2 + 0.06);
        }
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
    if (state === "Applied") return 0x38bdf8;
    if (state === "Printed") return 0x818cf8;
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
    fallbackCanvas.style.width = "100%";
    fallbackCanvas.style.height = "100%";
    host.appendChild(fallbackCanvas);
    fallbackContext = fallbackCanvas.getContext("2d");
    rendererMode = "fallback";
}

function renderFallback(snapshot) {
    const rect = host.getBoundingClientRect();
    fallbackCanvas.width = Math.max(1, rect.width);
    fallbackCanvas.height = Math.max(1, rect.height);
    const ctx = fallbackContext;
    const len = snapshot.conveyorLengthInches || 260;
    ctx.fillStyle = "#111827";
    ctx.fillRect(0, 0, fallbackCanvas.width, fallbackCanvas.height);
    const y = fallbackCanvas.height * 0.55;
    ctx.fillStyle = "#263244";
    ctx.fillRect(40, y, fallbackCanvas.width - 80, 34);
    for (const eye of snapshot.eyes ?? []) {
        const x = 40 + (eye.positionInches / len) * (fallbackCanvas.width - 80);
        ctx.fillStyle = eye.active ? "#f97316" : "#38bdf8";
        ctx.fillRect(x - 2, y - 70, 4, 104);
    }
    for (const carton of snapshot.cartons ?? []) {
        const x = 40 + (carton.positionInches / len) * (fallbackCanvas.width - 80);
        ctx.fillStyle = carton.state === "Verified" ? "#22c55e" : carton.state === "Rejected" ? "#ef4444" : "#c08457";
        ctx.fillRect(x - 18, y - 28, 36, 28);
        if ((carton.labels ?? []).some(l => l.applied)) {
            ctx.fillStyle = "#f8fafc";
            ctx.fillRect(x - 4, y - 26, 14, 8);
        }
    }
    ctx.fillStyle = "#e5e7eb";
    ctx.font = "14px system-ui";
    ctx.fillText(`2D fallback (WebGL/Three.js unavailable). ${snapshot.lastEvent ?? ""}`, 20, 28);
}
