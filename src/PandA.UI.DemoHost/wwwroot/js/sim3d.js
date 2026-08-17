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
const REST_SIDE_Z = 24;   // side arm parked belt-side of its body so the pad stays visible
const ARM_HALF = 10;      // half-length of the tamp arm mesh
const BELT_TOP_Y = 3.2;   // top surface of the belt (carton sits here)
const LABEL_W = 4;        // rendered label width along the travel axis (inches)
const LABEL_H = 4;        // rendered label height (inches)
const CARTON_BROWN = 0x9c6b3f;  // kraft cardboard — cartons keep this colour; status is shown as text

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
    // A fresh scene/root is created here; drop any stale mesh maps from a previous init so a line
    // switch (which re-runs start()) fully repopulates the new root. Otherwise entries whose ids are
    // stable across lines (e.g. the tracking eyes) stay parented to the discarded root and vanish.
    printers.clear();
    cartons.clear();
    eyes.clear();

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

// A warehouse label print-and-apply enclosure centred at belt-side z=`cz`, with a coloured top cap
// (`accent` distinguishes top vs side units), a control panel + indicator lights on the camera-facing
// face, a label supply reel on top, and a status beacon. Purely cosmetic — the tamp arm/pad are added
// by the caller and are unaffected.
function addPrinterEnclosure(parent, cz, accent) {
    const shell = new three.MeshStandardMaterial({ color: 0xb8c0cc, roughness: 0.6, metalness: 0.28 });
    const panelMat = new three.MeshStandardMaterial({ color: 0x1f2937, roughness: 0.5, metalness: 0.3 });
    const accentMat = new three.MeshStandardMaterial({ color: accent, roughness: 0.45, metalness: 0.3 });
    const metalMat = new three.MeshStandardMaterial({ color: 0x64748b, roughness: 0.5, metalness: 0.5 });
    const rollMat = new three.MeshStandardMaterial({ color: 0xf1f5f9, roughness: 0.85 });

    const body = new three.Mesh(new three.BoxGeometry(20, 22, 13), shell);
    body.position.set(0, 14, cz);
    parent.add(body);

    const cap = new three.Mesh(new three.BoxGeometry(21, 2.2, 14), accentMat);
    cap.position.set(0, 25.2, cz);
    parent.add(cap);

    const front = cz + 6.8;
    const panel = new three.Mesh(new three.BoxGeometry(9, 9, 0.6), panelMat);
    panel.position.set(-4, 16, front);
    parent.add(panel);
    const lightColors = [0x22c55e, 0xf59e0b, 0x38bdf8];
    for (let i = 0; i < lightColors.length; i++) {
        const c = lightColors[i];
        const light = new three.Mesh(
            new three.CylinderGeometry(0.5, 0.5, 0.4, 12),
            new three.MeshStandardMaterial({ color: c, emissive: c, emissiveIntensity: 0.85 })
        );
        light.rotation.x = Math.PI / 2;
        light.position.set(2 + i * 1.7, 19, front + 0.1);
        parent.add(light);
    }

    // Label supply reel on top (axis along X).
    const hub = new three.Mesh(new three.CylinderGeometry(4.6, 4.6, 6.4, 20), rollMat);
    hub.rotation.z = Math.PI / 2;
    hub.position.set(0, 28.6, cz);
    parent.add(hub);
    for (const dx of [-3.4, 3.4]) {
        const flange = new three.Mesh(new three.CylinderGeometry(4.9, 4.9, 0.5, 20), metalMat);
        flange.rotation.z = Math.PI / 2;
        flange.position.set(dx, 28.6, cz);
        parent.add(flange);
    }

    // Status beacon.
    const beaconBase = new three.Mesh(new three.CylinderGeometry(0.5, 0.5, 2, 10), metalMat);
    beaconBase.position.set(8, 25.6, cz);
    parent.add(beaconBase);
    const beacon = new three.Mesh(
        new three.CylinderGeometry(1, 1, 2.6, 12),
        new three.MeshStandardMaterial({ color: 0xf59e0b, emissive: 0xf59e0b, emissiveIntensity: 0.9, transparent: true, opacity: 0.85 })
    );
    beacon.position.set(8, 27.9, cz);
    parent.add(beacon);
}

// A white print-media texture with a barcode and optional caption — used for the label riding the tamp
// pad and the label stamped onto the carton, so a "printed" label reads as real media, not a red square.
function makeLabelTexture(caption) {
    const canvas = document.createElement("canvas");
    canvas.width = 128;
    canvas.height = 128;
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = "#f8fafc";
    ctx.fillRect(0, 0, 128, 128);
    ctx.fillStyle = "#0f172a";
    let x = 14;
    while (x < 114) {
        const w = 2 + Math.floor(Math.random() * 4);
        ctx.fillRect(x, 18, w, 58);
        x += w + 2 + Math.floor(Math.random() * 4);
    }
    if (caption) {
        ctx.fillStyle = "#0f172a";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        // Shrink the font so a longer LPN still fits within the label media.
        let fontPx = 26;
        do {
            ctx.font = `bold ${fontPx}px system-ui, sans-serif`;
            if (ctx.measureText(String(caption)).width <= 116 || fontPx <= 12) break;
            fontPx -= 2;
        } while (true);
        ctx.fillText(String(caption), 64, 100);
    }
    const texture = new three.CanvasTexture(canvas);
    texture.anisotropy = 4;
    return texture;
}

// Build a print-and-apply station. A TOP station has an overhead gantry with a tamp arm that
// extends DOWN onto the carton top; a SIDE station has a horizontal arm that extends IN toward
// the carton's near (+z) face. Each station is placed statically at its printer-eye X.
function buildStation(printer) {
    const top = String(printer.orientation).toLowerCase() === "top";
    const group = new three.Group();

    const frameMat = new three.MeshStandardMaterial({ color: 0x334155, roughness: 0.6, metalness: 0.4 });
    const tampMat = new three.MeshStandardMaterial({ color: 0xf59e0b, roughness: 0.35, metalness: 0.2 });

    const tamp = new three.Group();

    if (top) {
        // Enclosure beside the belt at -z, gantry over the belt centre, vertical tamp arm.
        addPrinterEnclosure(group, -24, 0x14b8a6);
        const gantry = new three.Mesh(new three.BoxGeometry(3.5, 3.5, 40), frameMat);
        gantry.position.set(0, 34, -6);
        group.add(gantry);
        const column = new three.Mesh(new three.BoxGeometry(4.5, 44, 4.5), frameMat);
        column.position.set(0, 22, -24);
        group.add(column);

        const arm = new three.Mesh(new three.BoxGeometry(3, ARM_HALF * 2, 3), frameMat);
        tamp.add(arm);
        const pad = new three.Mesh(new three.BoxGeometry(9, 1.4, 6), tampMat);
        pad.position.set(0, -ARM_HALF, 0);
        tamp.add(pad);
        tamp.position.set(0, REST_TAMP_Y, 0);
    } else {
        // Enclosure beside the belt at +z (camera-facing), horizontal arm extending in -z onto the side face.
        addPrinterEnclosure(group, 26, 0x6366f1);

        const arm = new three.Mesh(new three.BoxGeometry(3, 3, ARM_HALF * 2), frameMat);
        tamp.add(arm);
        const pad = new three.Mesh(new three.BoxGeometry(6, 9, 1.4), tampMat);
        pad.position.set(0, 0, -ARM_HALF);
        tamp.add(pad);
        tamp.position.set(0, BELT_TOP_Y + 8, REST_SIDE_Z);
    }

    group.add(tamp);

    // A "loaded" label riding the tamp pad: the SAME white label that gets stamped onto the carton, shown
    // on the pad's visible face from the moment the carton's leading edge hits the print point until it
    // is applied — so the operator watches the exact label print onto the head, travel, then transfer to
    // the carton. Styled to match the applied label (white print media + dark border).
    const loaded = new three.Mesh(
        new three.PlaneGeometry(LABEL_W, LABEL_H),
        new three.MeshBasicMaterial({ map: makeLabelTexture(), side: three.DoubleSide })
    );
    const loadedBorder = new three.Mesh(
        new three.PlaneGeometry(LABEL_W + 0.6, LABEL_H + 0.6),
        new three.MeshBasicMaterial({ color: 0x334155, side: three.DoubleSide })
    );
    loadedBorder.position.z = -0.05;
    loaded.add(loadedBorder);
    if (top) {
        // On the underside of the descending pad, facing the carton top.
        loaded.rotation.set(-Math.PI / 2, 0, 0);
        loaded.position.set(0, -ARM_HALF - 0.85, 0);
    } else {
        // On the carton-facing (-z) face of the side pad — the face that presses onto the box — so it
        // reads as the label about to be stamped, not one stuck to the printer body.
        loaded.rotation.set(0, Math.PI, 0);
        loaded.position.set(0, 0, -ARM_HALF - 0.85);
    }
    loaded.visible = false;
    tamp.add(loaded);

    group.userData.tamp = tamp;
    group.userData.loaded = loaded;
    group.userData.top = top;
    group.userData.printerId = printer.printerId;
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

// Ride the printed label on this printer's tamp, then reach onto the carton to stamp it — driven by
// the label's own onTamp/applied state, so only the printer that printed a given carton acts on it.
function animateStation(group, cartonList) {
    const tamp = group.userData.tamp;
    if (!tamp) {
        return;
    }

    const top = group.userData.top;
    const printerId = group.userData.printerId;
    const loaded = group.userData.loaded;

    // Pick the job this printer is actively serving: among cartons carrying a label for this printer
    // that is NOT yet applied, choose the one whose centre is nearest this station. Picking blindly the
    // first carton would leave the pad blank for an approaching carton whenever an already-applied
    // carton is still on the line ahead of it.
    let serving = null;
    let bestDist = Infinity;
    for (const carton of cartonList ?? []) {
        const label = (carton.labels ?? []).find(l => l.printerId === printerId);
        if (!label || label.applied) {
            continue;
        }
        const centerX = toSceneX(carton.positionInches + carton.lengthInches / 2);
        const dist = Math.abs(centerX - group.position.x);
        if (dist < bestDist) {
            bestDist = dist;
            serving = { carton, label };
        }
    }

    // The printed label sits on the pad from its print fire point until it's applied.
    if (loaded) {
        loaded.visible = !!serving && !!serving.label.onTamp && !serving.label.applied;
    }

    let under = null;
    if (serving) {
        const centerX = toSceneX(serving.carton.positionInches + serving.carton.lengthInches / 2);
        const near = Math.abs(centerX - group.position.x) < serving.carton.lengthInches / 2 + 3;
        // Extend to stamp only while the label is loaded and NOT yet applied. The instant it applies we
        // retract, revealing the label on the carton at the apply point instead of hiding it behind the pad.
        const stamping = serving.label.onTamp && !serving.label.applied;
        if (near && stamping) {
            under = serving.carton;
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
    const bodyMaterial = new three.MeshStandardMaterial({ color: 0x1f2937, roughness: 0.5, metalness: 0.45 });
    const faceMaterial = new three.MeshStandardMaterial({ color: 0xe2e8f0, roughness: 0.35, metalness: 0.2 });
    // A compact photoelectric sensor planted on the near conveyor edge (like a real photo-eye), low to the
    // belt, rather than a tall stalk. The beam still shoots across the belt and is recoloured on trip.
    const foot = new three.Mesh(new three.BoxGeometry(3.6, 1.2, 4.4), bodyMaterial);
    foot.position.set(0, 3.6, 12.6);
    group.add(foot);
    const housing = new three.Mesh(new three.BoxGeometry(2.6, 5.4, 3.4), bodyMaterial);
    housing.position.set(0, 6.9, 12.6);
    group.add(housing);
    const lens = new three.Mesh(new three.CylinderGeometry(0.9, 0.9, 0.5, 16), faceMaterial);
    lens.rotation.x = Math.PI / 2;
    lens.position.set(0, 7.2, 10.8);
    group.add(lens);
    const beam = new three.Mesh(
        new three.BoxGeometry(0.7, 0.5, 26),
        new three.MeshStandardMaterial({ color: 0x38bdf8, emissive: 0x0369a1, emissiveIntensity: 1.2 })
    );
    beam.name = "beam";
    beam.position.set(0, 7.2, -1);
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
        updateCartonStatus(group, carton);
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
        new three.MeshStandardMaterial({ color: CARTON_BROWN, roughness: 0.92, metalness: 0.02 })
    );
    body.position.y = BELT_TOP_Y + carton.heightInches / 2;
    group.add(body);
    group.userData.body = body;

    // A strip of packing tape along the top seam, for a cardboard-carton read.
    const tape = new three.Mesh(
        new three.BoxGeometry(carton.lengthInches, 0.12, 2.6),
        new three.MeshStandardMaterial({ color: 0xd9c08a, roughness: 0.7, metalness: 0.05 })
    );
    tape.position.y = BELT_TOP_Y + carton.heightInches + 0.06;
    group.add(tape);

    group.userData.labels = new Map();
    group.userData.ruler = buildRuler(carton);
    group.add(group.userData.ruler);
    group.userData.statusHeight = BELT_TOP_Y + carton.heightInches + 6;
    return group;
}

// Cartons stay cardboard-brown; their processing state is shown as a floating text badge above the box
// (rebuilt only when the state actually changes) instead of recolouring the carton itself.
function updateCartonStatus(group, carton) {
    if (group.userData.statusText === carton.state) return;
    group.userData.statusText = carton.state;
    if (group.userData.status) {
        group.remove(group.userData.status);
        group.userData.status.material.map?.dispose();
        group.userData.status.material.dispose();
    }
    const sprite = makeTextSprite(carton.state, 4.2, statusTextColor(carton.state), "rgba(15,23,42,0.92)");
    sprite.position.set(0, group.userData.statusHeight ?? (BELT_TOP_Y + 12), 0);
    group.add(sprite);
    group.userData.status = sprite;
}

// A lengthwise inch ruler pinned to the carton's top-front edge. 0 sits at the trailing (upstream)
// edge and counts up toward the leading edge, so an operator can zoom in and read exactly where a
// label landed. Major ticks (every 6") carry a number; the two ends are marked T (trailing) / L (leading).
function buildRuler(carton) {
    const length = carton.lengthInches;
    const topY = BELT_TOP_Y + carton.heightInches;
    const ruler = new three.Group();
    ruler.position.set(0, topY, carton.widthInches / 2 + 1.1);

    const barMat = new three.MeshBasicMaterial({ color: 0xe2e8f0 });
    const tickMat = new three.MeshBasicMaterial({ color: 0xcbd5e1 });
    const majorMat = new three.MeshBasicMaterial({ color: 0xf8fafc });

    const bar = new three.Mesh(new three.BoxGeometry(length, 0.25, 0.25), barMat);
    ruler.add(bar);

    for (let i = 0; i <= Math.round(length); i++) {
        const major = i % 6 === 0;
        const h = major ? 2.4 : 1.1;
        const tick = new three.Mesh(new three.BoxGeometry(0.18, h, 0.18), major ? majorMat : tickMat);
        tick.position.set(i - length / 2, h / 2, 0);
        ruler.add(tick);
        if (major) {
            const num = makeTextSprite(String(i), 2.4, "#e2e8f0", "rgba(15,21,35,0.0)");
            num.position.set(i - length / 2, h + 1.4, 0);
            ruler.add(num);
        }
    }

    const tEnd = makeTextSprite("T", 2.6, "#fca5a5", "rgba(15,21,35,0.0)");
    tEnd.position.set(-length / 2 - 2, 1.6, 0);
    ruler.add(tEnd);
    const lEnd = makeTextSprite("L", 2.6, "#86efac", "rgba(15,21,35,0.0)");
    lEnd.position.set(length / 2 + 2, 1.6, 0);
    ruler.add(lEnd);

    ruler.userData.markers = new Map();
    return ruler;
}

// Only render a label once it has been APPLIED — before that the carton is bare, so the
// operator watches the tamp physically deposit the label at the fire point.
function syncLabels(group, carton) {
    const labels = (carton.labels ?? []).filter(l => l.applied);
    const ruler = group.userData.ruler;
    const seen = new Set();
    for (const label of labels) {
        const key = `${label.labelType}-${label.lpn}`;
        seen.add(key);
        let entry = group.userData.labels.get(key);
        if (!entry) {
            const plane = new three.Mesh(
                new three.PlaneGeometry(LABEL_W, LABEL_H),
                new three.MeshBasicMaterial({ map: makeLabelTexture(label.lpn), side: three.DoubleSide })
            );
            const border = new three.Mesh(
                new three.PlaneGeometry(LABEL_W + 0.5, LABEL_H + 0.5),
                new three.MeshBasicMaterial({ color: 0x334155, side: three.DoubleSide })
            );
            border.position.z = -0.02;
            plane.add(border);
            const tag = makeTextSprite(label.applyPointNotation ?? "?", 3.4, "#e2e8f0", "rgba(15,23,42,0.92)");
            const marker = new three.Mesh(new three.BoxGeometry(0.4, 3.2, 0.4), new three.MeshBasicMaterial({ color: 0xf472b6 }));
            entry = { plane, tag, marker };
            group.userData.labels.set(key, entry);
            group.add(plane);
            group.add(tag);
            ruler?.add(marker);
            ruler?.userData.markers.set(key, marker);
        }

        // cartonOffsetInches is the fire-point REFERENCE, measured from the trailing edge:
        // L = n from leading edge, T = n from trailing edge, M = centre + n (toward leading).
        const edge = edgeOf(label.applyPointNotation);
        const reference = label.cartonOffsetInches ?? carton.lengthInches / 2;
        const referenceX = reference - carton.lengthInches / 2;
        // L/T are measured to the label's near edge; M is measured to the label centre.
        const half = LABEL_W / 2;
        let centerX = referenceX;
        if (edge === "L") centerX = referenceX - half;
        else if (edge === "T") centerX = referenceX + half;
        const limit = Math.max(0, carton.lengthInches / 2 - half);
        centerX = Math.max(-limit, Math.min(limit, centerX));

        const onTop = String(label.orientation).toLowerCase() === "top";
        if (onTop) {
            // Sit just above the packing-tape strip (tape top surface is at height + 0.12) so a
            // top-apply label reads as stuck ON the carton, not tucked under the tape.
            entry.plane.rotation.set(-Math.PI / 2, 0, 0);
            entry.plane.position.set(centerX, BELT_TOP_Y + carton.heightInches + 0.16, 0);
            entry.tag.position.set(centerX, BELT_TOP_Y + carton.heightInches + 5, 0);
        } else {
            entry.plane.rotation.set(0, 0, 0);
            entry.plane.position.set(centerX, BELT_TOP_Y + carton.heightInches / 2, carton.widthInches / 2 + 0.06);
            entry.tag.position.set(centerX, BELT_TOP_Y + carton.heightInches + 3, carton.widthInches / 2 + 0.06);
        }
        // Fire-point marker sits on the ruler at the exact reference point.
        entry.marker.position.set(referenceX, 1.6, 0);
    }
    for (const [key, entry] of group.userData.labels) {
        if (!seen.has(key)) {
            group.remove(entry.plane);
            group.remove(entry.tag);
            ruler?.remove(entry.marker);
            ruler?.userData.markers.delete(key);
            group.userData.labels.delete(key);
        }
    }
}

function edgeOf(notation) {
    const c = (notation ?? "").trim().slice(-1).toUpperCase();
    return c === "L" ? "L" : c === "T" ? "T" : "M";
}

// A camera-facing text label drawn on a canvas texture. `worldHeight` is its height in scene units.
function makeTextSprite(text, worldHeight, color, background) {
    const canvas = document.createElement("canvas");
    canvas.width = 256;
    canvas.height = 128;
    const ctx = canvas.getContext("2d");
    if (background && !background.endsWith("0.0)")) {
        ctx.fillStyle = background;
        roundRect(ctx, 8, 28, 240, 72, 16);
        ctx.fill();
    }
    ctx.fillStyle = color ?? "#f8fafc";
    ctx.font = "bold 72px system-ui, sans-serif";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText(text, 128, 64);
    const texture = new three.CanvasTexture(canvas);
    texture.anisotropy = 4;
    const material = new three.SpriteMaterial({ map: texture, transparent: true, depthTest: false, depthWrite: false });
    const sprite = new three.Sprite(material);
    sprite.scale.set(worldHeight * 2, worldHeight, 1);
    sprite.userData.text = text;
    return sprite;
}

function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
}

// Carton processing state → text colour for the floating status badge (the carton body stays brown).
function statusTextColor(state) {
    if (state === "Verified") return "#4ade80";
    if (state === "Rejected") return "#f87171";
    if (state === "Applied") return "#38bdf8";
    if (state === "Printed") return "#a5b4fc";
    if (state === "Scanned") return "#fbbf24";
    return "#e2e8f0";
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
