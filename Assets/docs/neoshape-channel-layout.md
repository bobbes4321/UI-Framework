# NeoShape Vertex-Channel Layout (the Tier-2 variant contract)

Every visual in Neo UI is an `NeoShape` — a single procedural SDF Graphic. There is exactly **one
shared material** (`Resources/NeoShape.shader`, "Neo/UI/Shape") for the whole project: an NeoShape
never gets its own material, because all of its shape parameters arrive **per vertex** in the mesh
channels, not as material properties. That is what lets the entire UI batch into one (or very few)
draw calls while still drawing rounded rects, ellipses, rings/arcs, glyphs, borders, gradients and
sprite fills.

This channel packing is therefore a **published, stable seam** — exactly like the spec schema. A
consuming project that wants a custom *Tier-2* shader effect (a `ShapeEffectDefinition` resolved by
`NeoShapeVariant`, `effect: { "id": "variant", "params": { "definition": "..." } }`) authors its
shader against this layout. As long as the variant shader reads these channels the same way, it sees
the same geometry the base shader sees, and the project ships a new look without forking the package.
The proof-of-concept variant shader is `Runtime/Graphics/Effects/NeoShapeDissolve.shader` —
copy its channel declarations verbatim.

> Tier-1 effects (glowPulse / sheenSweep / gradientCycle) do **not** touch the material — they only
> animate the same fields `OnPopulateMesh` already reads (softness, gradient angle, fill colors), so
> they stay on the shared batch (`IShapeEffectDescriptor.BatchSafe == true`). Only a Tier-2 *variant*
> swaps the material and so breaks the shared batch (`BatchSafe == false`) — a deliberate, named split
> shared per variant, never per instance.

## The contract

Packed by `NeoShape.OnPopulateMesh` (`Runtime/Graphics/NeoShape.cs`). One `UIVertex` is emitted per
corner of the (softness-expanded) quad; the four channels below are **identical across all four
vertices** — they describe the shape, not the corner. `uv0` is the only per-vertex-varying channel.

| Channel | Semantic (`xyzw`) |
|---|---|
| **uv0** (`TEXCOORD0`) | `xy` = local position in px, **rect-center origin** (so center = `(0,0)`); `zw` = rect **half-size** in px. |
| **uv1** (`TEXCOORD1`) | Corner radii in px, **shader corner order** `x=TR, y=BR, z=TL, w=BL`. Repurposed per shape mode: for **glyphs** (Checkmark/Chevron/Cross) `x` = stroke thickness px; for **ring/arc** `x` = band thickness px, `y` = arc **mid angle** in radians, `z` = **half-sweep** in radians (`PI` = full ring). |
| **uv2** (`TEXCOORD2`) | `x` = **packed mode** `shapeMode + 16*fillMode + 256*textureFit`; `y` = border width px; `z` = edge softness px (drives AA, and widened for shadow/glow); `w` = gradient angle in radians. |
| **uv3** (`TEXCOORD3`) | Border color (RGBA). |
| **tangent** (`TANGENT`) | Gradient **color B** (RGBA). Gradient color A is the standard vertex `color`. |
| `color` (`COLOR`) | Fill color A / vertex tint. |

### Decoding the packed mode (uv2.x)

`OnPopulateMesh` writes `packedMode = shaderShape + 16*(int)fillMode + 256*(int)textureFit`. Unpack
in the fragment shader exactly as the base shader does:

```hlsl
float packedMode = IN.params.x;
float texFit    = floor(packedMode / 256.0 + 0.001); // 1 = cover-crop sprite fill
packedMode     -= texFit * 256.0;
float fillMode  = floor(packedMode / 16.0 + 0.001);  // 0 solid, 1 linear, 2 radial
float shapeMode = packedMode - fillMode * 16.0;       // 0 rrect, 1 ellipse, glyphs, 5+ ring/arc
```

Shape-mode constants live in `NeoShape.cs` (`ShaderRoundedRect`/`ShaderEllipse`/`ShaderArc`/the glyph
ids). The base SDF helpers (`sdRoundedRect`, `sdEllipse`, the arc segment, the glyph segments) are the
canonical decode of `uv1` for each mode — mirror them if your variant needs the silhouette.

## Authoring a Tier-2 variant

1. Copy the `appdata_t` struct (POSITION / COLOR / `TEXCOORD0..3` / TANGENT) from
   `NeoShapeDissolve.shader` so your shader binds the same channels NeoShape packs. **Do not** reorder
   or drop channels — the mesh always carries all five.
2. Reconstruct the base silhouette from `uv0` + `uv1` + `uv2` (reuse `sdRoundedRect` etc.), then layer
   your effect on top (the dissolve shader adds a noise clip + edge glow + scanline as the worked
   example).
3. Keep the same render state block (Stencil / `Blend One OneMinusSrcAlpha` / `ZTest
   [unity_GUIZTestMode]` / premultiplied `col.rgb *= col.a`) so masking and sort order match the rest
   of the UI.
4. Expose tunables as material properties; `ShapeEffectDefinition.ApplyDefaults` seeds them onto the
   **shared** variant material (one material per definition, not per instance).
5. Register the look as a `ShapeEffectDefinition` asset addressed by a stable string `Id`; reference it
   from a spec via `effect: { "id": "variant", "params": { "definition": "<Id>" } }`.

A shader error in a variant is low-blast-radius — it never breaks C# compilation, and the base
"Neo/UI/Shape" path is unaffected.
