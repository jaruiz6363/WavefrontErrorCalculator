# Hopkins's surface-contribution method

WEC computes the wave aberration W three independent ways, as a check on itself:

1. **Optical path** (`wec-method.md`): each ray's optical path is summed surface by surface, and W is the chief ray's path minus the ray's, measured to the reference sphere.
2. **Rayces's relation** (`rayces-method.md`): W is integrated across the pupil from where the rays go in image space, with no optical path at all.
3. **Hopkins's surface contributions** (this document): the difference between a ray and the chief ray is built up surface by surface, from where each ray meets each surface and which way it goes.

If WEC is right, all three give the same W. Section 4 shows that they do.

Reference: H. H. Hopkins, "The wave aberration associated with skew rays", *Proc. Phys. Soc. B* 65, 934 (1952).

## 1. The idea

![Image space: a ray and the chief ray, their invariant focus M, the image point I, and the two ways of moving the reference from M to I - Hopkins's (section 3) and Tatian's (section 5). Schematic, in one plane, aberrations exaggerated.](figures/hopkins-geometry.svg)

The optical-path difference between two rays depends on where it is measured. Hopkins showed that for any two rays there is a point where it does not: measured to any sphere centred on the mid-point M of their shortest join, the path difference between the two rays is the same however far the light has travelled. He called M the rays' *invariant focus*. When two rays cross, M is where they cross.

So the aberration Ω of a ray against the chief ray, referred to their invariant focus, is a well-defined number at every stage of the lens, and Hopkins gives what each surface adds to it.

## 2. What each surface adds (Hopkins 1952, eq. 7)

> **Δ(Ω) = Δ(N e),  e = Σ(λ + λ̄)(X − X̄) / (1 + Σλλ̄)**

- (X, Y, Z) and (X̄, Ȳ, Z̄): where the ray and the chief ray meet the surface;
- λ, μ, ν and λ̄, μ̄, ν̄: their direction cosines; Σ runs over the three components;
- Δ: the value after the surface (index N′, directions after refraction) minus the value before it (index N, directions before).

Starting from zero in object space (both rays leave the same object point), the sum over the surfaces is Ω in image space. Only the points of incidence and the directions enter: no optical path is summed.

## 3. From the invariant focus to the image point (Hopkins 1952, eq. 13)

Ω is referred to M, which is different for every ray. The wavefront aberration W is referred to one point for all rays, the image point I, on the usual reference sphere: centred on I and passing through E′, where the chief ray crosses the exit pupil. Hopkins's focal shift moves the reference there: it is the ray's optical path, along the ray, between the sphere about I through E′ and the sphere about M through the same E′.

That is the same definition of W the optical-path method uses (`wec-method.md` §2), so the two can be compared ray by ray. WEC takes the focal shift exactly. Hopkins's printed formula, eq. 13, drops the square of the distance between the two spheres; that approximation is small on ordinary lenses (10⁻⁵ to 10⁻² wave on the test lenses here) and grows with the aberration.

## 4. The three methods compared

All three methods on the same rays and the same definition of W: ray aiming real (aimed at the real stop), the reference sphere through the real exit pupil (WEC's default, `RealChief`), the 857 pupil points of every field (a grid of spacing 1/16 and 60 rim points), at the primary wavelength. Each entry is the largest difference from the optical-path W, over all those points and fields.

| Lens | Largest W (waves) | Rayces − optical path (waves) | Hopkins − optical path (waves) |
|---|---|---|---|
| Kingslake double Gauss | 6.7 | 1.0×10⁻¹⁰ | 7.9×10⁻¹¹ |
| Cooke triplet | 3.7 | 1.0×10⁻⁹ | 1.0×10⁻⁹ |
| US8264785 | 3.0 | 7.3×10⁻⁹ | 7.3×10⁻⁹ |
| 1:1 relay | 3.6 | 2.7×10⁻¹⁰ | 2.8×10⁻¹⁰ |
| NA 0.3 objective | 1.6 | 1.3×10⁻¹⁰ | 1.3×10⁻¹⁰ |
| `FastSinglet` | 1,424 | 2.7×10⁻¹⁰ | 7.4×10⁻¹¹ |
| `FastSinglet_Defocused` | 1,519 | 9.5×10⁻¹⁰ | 6.2×10⁻¹¹ |
| `ShortPupilSinglet` | 3,928 | 1.3×10⁻¹⁰ | 6.0×10⁻¹⁰ |

The differences, 10⁻¹¹ to 10⁻⁹ wave, are at the level of rounding: the three methods give the same W, on ordinary lenses and on singlets with thousands of waves. US8264785's slightly larger floor is shared by both checks and most likely comes from the ray trace's intersections with its aspheres.

## 5. Tatian's note

B. Tatian, "A comment on the wave aberration formula of H. H. Hopkins", *Optica Acta* 19, 79 (1972), keeps Hopkins's surface formula and replaces the focal shift of §3. Instead of a sphere through the exit pupil, he refers each ray to the foot of the perpendicular from the image point I (Hamilton's mixed characteristic, after Luneburg's diffraction integral), so that W does not depend on where the exit pupil is. That is the reference sphere taken to an infinite radius: WEC's `ExitPupil=Infinite`, and OpticStudio's Reference OPD "Infinity". With Tatian's focal shift, Hopkins's method agrees with WEC's optical path on that reference to 10⁻⁸ wave (`docs/verification.md`).

It is a different choice of reference, not a different calculation. On a lens with a few waves of aberration, W on the infinite reference differs from W on the exit-pupil sphere by a large fraction of a wave (0.5 wave on the double Gauss at 14°), so two values of W can be compared only when both use the same reference.

## 6. Limits

- Centred systems: a tilted or decentred surface is refused.
- Not afocal.
- Mirrors need no special case (the indices are unsigned and the directions are those the light travels), but none of the test lenses has one.

## 7. Using it

```
wfe hopkins Cooke_40deg_FC.zmx --1952 --set RayAiming=RealStop --set ExitPupil=RealChief --set ChiefRay=StopCenter
```

Per field: the largest difference between Hopkins's method and the optical path on the same sphere, exactly and with eq. 13 as printed, and the largest |W|. Without `--1952` it uses Tatian's focal shift and compares on the infinite reference. `--csv` writes every point.
