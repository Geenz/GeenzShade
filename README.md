# GeenzShade - Unity Package Manager Installation

## Installation via Unity Package Manager

### Option 1: Install via Git URL
1. Open Unity Package Manager (Window > Package Manager)
2. Click the '+' button in the top-left corner
3. Select "Add package from git URL..."
4. Enter: `https://github.com/Geenz/GeenzShade.git#upm`
5. Click 'Add'

### Option 2: Install specific version
To install a specific version, append the version tag:
- `https://github.com/Geenz/GeenzShade.git#upm/v0.0.2`

### Option 3: Add to manifest.json
Add this line to your `Packages/manifest.json` dependencies:
```json
"com.geenz.geenzshade": "https://github.com/Geenz/GeenzShade.git#upm"
```

---

# GeenzShade
This a collection of shaders that I'm building out with time.  These are just the basics - advanced tooling (such as texture packing) is planned as a paid add on that you can obtain through my [Patreon](https://www.patreon.com/c/voidanchor).

If you use my work, please considering supporting further development by contributing to my [Patreon](https://www.patreon.com/c/voidanchor)!  You not only will support further development of my various shaders, but also various tools, behind the scenes development progress, and more!

Included shaders are below.

## GzPBR
A comprehensive, physically-based rendering (PBR) shader system for Unity that implements the glTF 2.0 specification with advanced material extensions. GeenzShade provides a high-quality, artist-friendly shader solution with support for modern rendering techniques and VRChat-specific optimizations.

### Features

#### Core PBR Implementation
- **Full glTF 2.0 Compliance**: Implements the complete glTF 2.0 core material specification
- **Metallic-Roughness Workflow**: Industry-standard PBR workflow with metallic and roughness parameters
- **ORM Texture Packing**: Efficient texture usage with Occlusion-Roughness-Metallic packed textures
- **Normal Mapping**: Tangent-space normal maps with adjustable intensity
- **Emission**: HDR emissive materials with strength control

#### Advanced Material Extensions

##### Clearcoat
- Simulates a thin, clear layer over the base material (like car paint or varnished wood)
- Separate clearcoat normal map support
- Independent roughness control for the clearcoat layer

##### Iridescence
- Thin-film interference effects (soap bubbles, oil slicks, butterfly wings)
- Wavelength-dependent color shifts
- Configurable thickness range
- IOR control for film material
- Note: currently ignores interactions with claercoat layers when enabled.

##### Sheen
- Fabric and cloth-like materials with rim lighting
- Artistic rim boost control for enhanced backscatter effects
- Color and roughness control

##### Specular Extension
- Override default Fresnel behavior for artistic control
- Separate specular color and strength
- Useful for stylized non-metallic materials

##### Diffuse Transmission
- Thin translucent surfaces (leaves, paper, fabric)
- Colored transmission with per-pixel control
- Works best with double-sided rendering

#### Rendering Modes
- **Opaque**: Standard solid materials
- **Cutout**: Alpha-tested transparency with configurable threshold
- **Transparent**: Full alpha blending with depth fade support
- **Additive**: Light-accumulating blend mode
- **Multiply**: Darkening blend mode
- **Premultiplied Alpha**: Correct compositing for UI and special effects

#### Advanced Rendering Features

##### Specular Antialiasing
- Reduces specular aliasing at grazing angles
- Distance-based roughness adjustment
- Edge falloff control for artistic tuning
- Currently is not normal map aware (outside of grazing angles)

##### Environment Lighting
- Reflection probe sampling with fallback support
- Spherical harmonics for diffuse lighting
- HDR cubemap fallback when probes are unavailable
- Dominant light extraction from spherical harmonics to help make static lighting "pop" more

##### VRChat Features
- **Light Volume Support**: Full integration with VRChat's light volume system

#### Texture Channels

##### Base Color Texture
- **RGB**: Albedo/diffuse color
- **A**: Alpha (opacity) for transparency modes

##### ORM Texture (Occlusion-Roughness-Metallic)
- **R**: Ambient occlusion (0=occluded, 1=unoccluded)
- **G**: Roughness factor (0=glossy, 1=rough)
- **B**: Metallic factor (0=dielectric, 1=metal)

##### Clearcoat/Iridescence Texture
- **R**: Clearcoat intensity factor
- **G**: Clearcoat roughness factor
- **B**: Iridescence intensity factor
- **A**: Iridescence thickness factor

##### Sheen Texture
- **RGB**: Sheen color tint
- **A**: Sheen roughness factor

##### Diffuse Transmission Texture
- **RGB**: Transmission color modulation
- **A**: Transmission factor (0=opaque, 1=fully transmissive)

### Installation

1. Import the GeenzShade package into your Unity project
2. The shader will appear under `GeenzShade/GzPBR` in the shader dropdown
3. Materials using the shader will automatically use the custom GUI for easy configuration

### Usage

#### Creating a New Material
1. Create a new Material in Unity
2. Select `GeenzShade/GzPBR` from the shader dropdown
3. The custom inspector will provide organized sections for all features

#### Performance Considerations

- GzPBR has many toggles for specific features in the shader.  Being an uber shader, it's important to only use what you need.
  - For example, you're unlikely to need clear coat on every single surface - so you might want to disable clear coat in the material's inspector.
- For now, things like iridescence utilize the full calculation and no lookup textures.
  - This shouldn't be particularly painful on modern GPUs, but it's worth investigating performance vs. a LUT.  I have not yet done this.
- Texture samplers are generally conservative when using the included material GUI
  - I've tried to make this pretty automatic for artists - you shouldn't really need to worry about how many samplers the shader has provided you're conservative with how many texture fields you populate.

### Shader Variants and Keywords

The shader uses local keywords to minimize global keyword usage:
- Material features are controlled via `USE_*` keywords
- Rendering modes use `_RENDERMODE_*` keywords
- Only essential multi_compile variants are used

### Custom Editor GUI

The material inspector provides:
- Organized foldout sections for features
- Context-sensitive property display
- Automatic keyword management
- Helpful tooltips and descriptions
- Warning messages for incorrect settings

### Technical Details

#### Lighting Model
- GGX BRDF for specular
- Lambert diffuse with energy conservation
- Proper Fresnel equations using IOR
- Multi-layer material stacking

## License

This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

Copyright (c) 2025 Geenz

## Credits

Unity shaders created by Geenz

Based on the glTF 2.0 Specification, reference implementation, and extensions.  glTF is owned by the Khronos Group.  You can find the spec and its extensions [in the Khronos Group's glTF repo.](https://github.com/KhronosGroup/glTF/)
