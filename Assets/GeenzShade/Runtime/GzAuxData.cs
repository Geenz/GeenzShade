/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Copyright (c) 2025 Geenz
 */

using UnityEngine;

namespace GeenzShade
{
    /// <summary>Face cull mode encoded in the aux data texture's B channel.</summary>
    public enum GzFaceCull { Off = 0, Back = 1, Front = 2 }

    /// <summary>Scalar material params carried by the GzPBR auxiliary data texture.</summary>
    public struct GzAuxParams
    {
        public float ior;            // remap range is configurable (default 0..4)
        public float iridescenceIOR; // remap range is configurable (default 0..4)
        public GzFaceCull faceCull;
        public float sheenRimBoost;  // 0..10
    }

    /// <summary>
    /// Encode/decode for the GzPBR auxiliary data texture. This is the canonical
    /// API for project tooling that authors aux textures.
    ///
    /// Channel layout (must match GzMaterialSampling.cginc):
    ///   R = IOR (0..4), G = Iridescence IOR (0..4),
    ///   B = Face Cull (0=off, 0.5=back, 1=front), A = Sheen Rim Boost (0..10).
    ///
    /// The texture must be sampled as LINEAR data (sRGB off) and is best left
    /// uncompressed so the packed scalar values survive intact.
    /// </summary>
    public static class GzAuxData
    {
        public const float IorMin = 0f, IorMax = 4f;
        public const float IridescenceIorMin = 0f, IridescenceIorMax = 4f;
        public const float SheenRimMin = 0f, SheenRimMax = 10f;

        /// <summary>Encode a parameter set into a normalized linear RGBA texel using the
        /// canonical ranges (IOR 0-4, Iridescence IOR 0-4, Sheen Rim 0-10).</summary>
        public static Color Encode(float ior, float iridescenceIOR, GzFaceCull faceCull, float sheenRimBoost)
            => Encode(ior, iridescenceIOR, faceCull, sheenRimBoost,
                      IorMin, IorMax, IridescenceIorMin, IridescenceIorMax, SheenRimMin, SheenRimMax);

        /// <summary>Encode with explicit remap ranges. The shader's decode must use the same
        /// ranges for the values to round-trip correctly.</summary>
        public static Color Encode(float ior, float iridescenceIOR, GzFaceCull faceCull, float sheenRimBoost,
            float iorMin, float iorMax, float iridIorMin, float iridIorMax, float sheenRimMin, float sheenRimMax)
        {
            return new Color(
                Mathf.InverseLerp(iorMin, iorMax, ior),
                Mathf.InverseLerp(iridIorMin, iridIorMax, iridescenceIOR),
                (int)faceCull * 0.5f,            // 0=off, 1=back, 2=front -> 0, 0.5, 1
                Mathf.InverseLerp(sheenRimMin, sheenRimMax, sheenRimBoost));
        }

        public static Color Encode(GzAuxParams p)
            => Encode(p.ior, p.iridescenceIOR, p.faceCull, p.sheenRimBoost);

        /// <summary>Decode a texel back to parameters (inverse of Encode and the shader).</summary>
        public static GzAuxParams Decode(Color c)
        {
            return new GzAuxParams
            {
                ior = Mathf.Lerp(IorMin, IorMax, c.r),
                iridescenceIOR = Mathf.Lerp(IridescenceIorMin, IridescenceIorMax, c.g),
                faceCull = (GzFaceCull)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 2f),
                sheenRimBoost = Mathf.Lerp(SheenRimMin, SheenRimMax, c.a),
            };
        }
    }
}
