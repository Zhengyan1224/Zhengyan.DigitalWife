let ammoPromise;
const worlds = new Map();
let nextWorldId = 0;

export async function initialize(factory, options = {}) {
    ammoPromise ??= (async () => {
        if (!factory) {
            const source = new URL('../vendor/ammo/ammo.wasm.js', import.meta.url);
            await new Promise((resolve, reject) => {
                const script = document.createElement('script');
                script.src = source.href;
                script.onload = resolve;
                script.onerror = () => reject(new Error('Bullet WASM loader failed to load.'));
                document.head.appendChild(script);
            });
            factory = globalThis.Ammo;
            options = { locateFile: name => new URL(name, source).href };
        }
        const ammo = await factory(options);
        if (!ammo.btGeneric6DofSpringConstraint) throw new Error('Bullet spring constraints are unavailable.');
        return ammo;
    })();
    return await ammoPromise;
}

function floatData(bytes) {
    if (Array.isArray(bytes)) return new Float32Array(bytes);
    if (!(bytes instanceof Uint8Array) || bytes.byteLength % 4) throw new Error('Invalid Bullet target buffer.');
    const aligned = bytes.byteOffset % 4 ? bytes.slice() : bytes;
    return new Float32Array(aligned.buffer, aligned.byteOffset, aligned.byteLength / 4);
}

export async function createWorld(definition) {
    const A = await initialize();
    const owned = [], own = object => { owned.push(object); return object; };
    const configuration = own(new A.btDefaultCollisionConfiguration());
    const dispatcher = own(new A.btCollisionDispatcher(configuration));
    const broadphase = own(new A.btDbvtBroadphase());
    const solver = own(new A.btSequentialImpulseConstraintSolver());
    const world = own(new A.btDiscreteDynamicsWorld(dispatcher, broadphase, solver, configuration));
    const vector = own(new A.btVector3(0,0,0)), zero = own(new A.btVector3(0,0,0));
    const target = own(new A.btTransform()), read = own(new A.btTransform());
    const quaternion = own(new A.btQuaternion(0,0,0,1));
    const setVector = values => { vector.setValue(values[0],values[1],values[2]); return vector; };
    const setTransform = (transform, matrix) => {
        // Numerics stores a row-vector rotation. Convert it to a Bullet
        // quaternion without allocating transient WASM matrix buffers.
        const m00=matrix[0],m11=matrix[5],m22=matrix[10],trace=m00+m11+m22;
        let x,y,z,w,s;
        if (trace>0) {
            s=Math.sqrt(trace+1); w=s*.5; s=.5/s;
            x=(matrix[6]-matrix[9])*s; y=(matrix[8]-matrix[2])*s; z=(matrix[1]-matrix[4])*s;
        } else if (m00>=m11 && m00>=m22) {
            s=Math.sqrt(1+m00-m11-m22); x=s*.5; s=.5/s;
            y=(matrix[1]+matrix[4])*s; z=(matrix[2]+matrix[8])*s; w=(matrix[6]-matrix[9])*s;
        } else if (m11>m22) {
            s=Math.sqrt(1+m11-m00-m22); y=s*.5; s=.5/s;
            x=(matrix[4]+matrix[1])*s; z=(matrix[9]+matrix[6])*s; w=(matrix[8]-matrix[2])*s;
        } else {
            s=Math.sqrt(1+m22-m00-m11); z=s*.5; s=.5/s;
            x=(matrix[8]+matrix[2])*s; y=(matrix[9]+matrix[6])*s; w=(matrix[1]-matrix[4])*s;
        }
        quaternion.setValue(x,y,z,w);
        transform.setIdentity();
        transform.setRotation(quaternion);
        transform.setOrigin(setVector([matrix[12],matrix[13],matrix[14]]));
    };
    world.setGravity(setVector(definition.gravity));

    // Reserve an unused PMX group for the plane. Adding that bit only to
    // masks reproduces PC's always-collidable floor without changing any
    // PMX body/body filtering (Ammo's stock IDL has no overlap-filter hook).
    const usedGroups = definition.bodies.reduce((mask, body) => mask | (1 << body.group), 0);
    let floorGroup = 0;
    for (let bit=15;bit>=0;bit--) if (!(usedGroups & (1<<bit))) { floorGroup=1<<bit; break; }
    if (!floorGroup) throw new Error('Bullet requires one unused PMX collision group for the ground plane.');
    const floorShape = own(new A.btStaticPlaneShape(setVector([0,1,0]),0));
    target.setIdentity();
    const floorMotion = own(new A.btDefaultMotionState(target));
    const floorInfo = new A.btRigidBodyConstructionInfo(0,floorMotion,floorShape,zero);
    const floor = own(new A.btRigidBody(floorInfo));
    A.destroy(floorInfo);
    world.addRigidBody(floor,floorGroup,65535);

    const bodies = definition.bodies.map(source => {
        let shape;
        if (source.shape === 0) shape = new A.btSphereShape(source.size[0]);
        else if (source.shape === 1) shape = new A.btBoxShape(setVector(source.size));
        else if (source.shape === 2) shape = new A.btCapsuleShape(source.size[0],source.size[1]);
        else throw new Error(`Unsupported PMX collision shape: ${source.shape}`);
        own(shape);
        setTransform(target, source.transform);
        const motion = own(new A.btDefaultMotionState(target));
        vector.setValue(0,0,0);
        if (source.mass > 0) shape.calculateLocalInertia(source.mass,vector);
        const info = new A.btRigidBodyConstructionInfo(source.mass,motion,shape,vector);
        info.set_m_linearDamping(source.linearDamping);
        info.set_m_angularDamping(source.angularDamping);
        info.set_m_restitution(source.restitution);
        info.set_m_friction(source.friction);
        info.set_m_additionalDamping(true);
        const body = own(new A.btRigidBody(info));
        A.destroy(info);
        body.setSleepingThresholds(.01,Math.PI/1800);
        body.setActivationState(4); // DISABLE_DEACTIVATION, matching PC/Android.
        if (source.operation === 0) body.setCollisionFlags(body.getCollisionFlags() | 2);
        const group = 1 << source.group, mask = source.mask | floorGroup;
        world.addRigidBody(body,group,mask);
        return { body,motion,source,group,mask };
    });
    const frameA = own(new A.btTransform()), frameB = own(new A.btTransform());
    const constraints = definition.joints.map(joint => {
        setTransform(frameA,joint.frameA); setTransform(frameB,joint.frameB);
        const constraint = own(new A.btGeneric6DofSpringConstraint(bodies[joint.a].body,bodies[joint.b].body,frameA,frameB,true));
        constraint.setLinearLowerLimit(setVector(joint.linearLower));
        constraint.setLinearUpperLimit(setVector(joint.linearUpper));
        constraint.setAngularLowerLimit(setVector(joint.angularLower));
        constraint.setAngularUpperLimit(setVector(joint.angularUpper));
        for (let axis=0;axis<6;axis++) if (joint.springs[axis] !== 0) {
            constraint.enableSpring(axis,true); constraint.setStiffness(axis,joint.springs[axis]);
        }
        world.addConstraint(constraint,true);
        return constraint;
    });
    const id = ++nextWorldId;
    worlds.set(id,{ A,world,bodies,constraints,floor,owned,vector,zero,quaternion,target,read,setTransform,setVector });
    return id;
}

export function stepWorld(id, bytes, deltaSeconds, reset) {
    const state = worlds.get(id);
    if (!state) throw new Error('Bullet world has been disposed.');
    const { A,world,bodies,constraints,target,read,zero } = state;
    const matrices = floatData(bytes);
    if (matrices.length !== bodies.length * 16) throw new Error('Bullet target count does not match the PMX bodies.');
    if (reset) {
        // Removing/reinserting bodies clears their old broadphase pairs and
        // contact manifolds, equivalent to PC CleanProxyFromPairs on reset.
        for (const constraint of constraints) world.removeConstraint(constraint);
        for (const entry of bodies) world.removeRigidBody(entry.body);
    }
    for (let i=0;i<bodies.length;i++) {
        const entry = bodies[i], body = entry.body;
        if (reset || entry.source.operation === 0) {
            state.setTransform(target,matrices.subarray(i*16,i*16+16));
            entry.motion.setWorldTransform(target);
            if (reset) {
                body.setWorldTransform(target);
                body.setCenterOfMassTransform(target);
                body.setInterpolationWorldTransform(target);
                body.setLinearVelocity(zero); body.setAngularVelocity(zero); body.clearForces();
            }
        }
        body.activate(true);
    }
    if (reset) {
        for (const entry of bodies) world.addRigidBody(entry.body,entry.group,entry.mask);
        for (const constraint of constraints) world.addConstraint(constraint,true);
        world.stepSimulation(1/60,1,1/60);
    }
    if (deltaSeconds > 0) world.stepSimulation(Math.min(deltaSeconds,1/15),10,1/120);
    const result = [];
    for (let i=0;i<bodies.length;i++) {
        const entry = bodies[i];
        if (entry.source.operation === 0) continue;
        entry.motion.getWorldTransform(read);
        const position = read.getOrigin(), rotation = state.quaternion;
        read.getBasis().getRotation(rotation);
        result.push(i,position.x(),position.y(),position.z(),rotation.x(),rotation.y(),rotation.z(),rotation.w());
    }
    return result;
}

export function applyImpulse(id, index, velocity, torque, local) {
    const state = worlds.get(id), entry = state?.bodies[index];
    if (!entry || entry.source.operation === 0) return;
    if (local) {
        const q = entry.body.getWorldTransform().getRotation();
        // Android converts the body basis back to the renderer before
        // transforming local impulse vectors, then submits those to Bullet.
        const rotate = v => {
            const x=-q.x(),y=-q.y(),z=q.z(),w=q.w();
            const tx=2*(y*v[2]-z*v[1]),ty=2*(z*v[0]-x*v[2]),tz=2*(x*v[1]-y*v[0]);
            return [v[0]+w*tx+y*tz-z*ty,v[1]+w*ty+z*tx-x*tz,v[2]+w*tz+x*ty-y*tx];
        };
        velocity=rotate(velocity); torque=rotate(torque); state.A.destroy(q);
    }
    entry.body.activate(true);
    entry.body.applyCentralImpulse(state.setVector(velocity));
    entry.body.applyTorqueImpulse(state.setVector(torque));
}

export function destroyWorld(id) {
    const state = worlds.get(id);
    if (!state) return;
    worlds.delete(id);
    for (const constraint of state.constraints) state.world.removeConstraint(constraint);
    for (const entry of state.bodies) state.world.removeRigidBody(entry.body);
    state.world.removeRigidBody(state.floor);
    for (let i=state.owned.length-1;i>=0;i--) state.A.destroy(state.owned[i]);
}
