using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.VFX;

namespace UnityEditor.VFX.Block
{
    [VFXInfo(category = "Position", variantProvider = typeof(PositionBaseProvider))]
    class PositionCone : PositionBase
    {
        public enum HeightMode
        {
            Base,
            Volume
        }

        [VFXSetting, Tooltip("Controls whether particles are spawned on the base of the cone, or throughout the entire volume.")]
        public HeightMode heightMode;

        public override string name { get { return string.Format(base.name, "Cone"); } }
        protected override float thicknessDimensions { get { return 2.0f; } }

        public class InputProperties
        {
            [Tooltip("Sets the cone used for positioning the particles.")]
            public ArcCone ArcCone = ArcCone.defaultValue;
        }

        public class CustomProperties
        {
            [Range(0, 1), Tooltip("Sets the position along the height to emit particles from when ‘Custom Emission’ is used.")]
            public float HeightSequencer = 0.0f;
            [Range(0, 1), Tooltip("Sets the position on the arc to emit particles from when ‘Custom Emission’ is used.")]
            public float ArcSequencer = 0.0f;
        }

        protected override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get
            {
                var properties = PropertiesFromType("InputProperties");

                if (supportsVolumeSpawning)
                {
                    if (positionMode == PositionMode.ThicknessAbsolute || positionMode == PositionMode.ThicknessRelative)
                        properties = properties.Concat(PropertiesFromType("ThicknessProperties"));
                }

                if (spawnMode == SpawnMode.Custom)
                    properties = properties.Concat(PropertiesFromType("CustomProperties"));

                return properties;
            }
        }

        public override IEnumerable<VFXNamedExpression> parameters
        {
            get
            {
                foreach (var p in GetExpressionsFromSlots(this).Where(      e =>    e.name == "ArcCone_arc"
                                                                                ||  e.name == "ArcCone_radius0"
                                                                                ||  e.name == "ArcCone_radius1"
                                                                                ||  e.name == "ArcCone_height"
                                                                                ||  e.name == "ArcSequencer"))
                    yield return p;

                yield return new VFXNamedExpression(CalculateVolumeFactor(positionMode, 0, 1), "volumeFactor");

                VFXExpression radius0 = inputSlots[0][3].GetExpression();
                VFXExpression radius1 = inputSlots[0][4].GetExpression();
                VFXExpression height = inputSlots[0][5].GetExpression();
                VFXExpression tanSlope = (radius1 - radius0) / height;
                VFXExpression slope = new VFXExpressionATan(tanSlope);
                yield return new VFXNamedExpression(new VFXExpressionCombine(new VFXExpression[] { new VFXExpressionSin(slope), new VFXExpressionCos(slope) }), "sincosSlope");

                VFXExpression center = inputSlots[0][0].GetExpression();

                var eulerAngle = inputSlots[0][1].GetExpression();
                var zeroF3 = VFXOperatorUtility.ZeroExpression[VFXValueType.Float3];
                var oneF3 = VFXOperatorUtility.OneExpression[VFXValueType.Float3];

                VFXExpression rotationMatrix = new VFXExpressionTRSToMatrix(zeroF3, eulerAngle, oneF3);
                VFXExpression i = new VFXExpressionMatrixToVector3s(rotationMatrix, VFXValue.Constant(0));
                VFXExpression j = new VFXExpressionMatrixToVector3s(rotationMatrix, VFXValue.Constant(1));
                VFXExpression k = new VFXExpressionMatrixToVector3s(rotationMatrix, VFXValue.Constant(2));

                var transformMatrix = new VFXExpressionVector3sToMatrix(i, k, j, center);
                yield return new VFXNamedExpression(transformMatrix, "transformMatrix");
            }
        }

        protected override bool needDirectionWrite => true;

        public override string source
        {
            get
            {
                string outSource = "";

                if (spawnMode == SpawnMode.Random)
                    outSource += @"float theta = ArcCone_arc * RAND;";
                else
                    outSource += @"float theta = ArcCone_arc * ArcSequencer;";

                outSource += @"
float rNorm = sqrt(volumeFactor + (1 - volumeFactor) * RAND);

float2 sincosTheta;
sincos(theta, sincosTheta.x, sincosTheta.y);
float2 pos = (sincosTheta * rNorm);
";

                if (heightMode == HeightMode.Base)
                {
                    outSource += @"
float hNorm = 0.0f;
";
                }
                else if (spawnMode == SpawnMode.Random)
                {
                    float distributionExponent = positionMode == PositionMode.Surface ? 2.0f : 3.0f;
                    outSource += $@"
float hNorm = 0.0f;
if (abs(ArcCone_radius0 - ArcCone_radius1) > VFX_EPSILON)
{{
    // Uniform distribution on cone
    float heightFactor = ArcCone_radius0 / max(VFX_EPSILON,ArcCone_radius1);
    float heightFactorPow = pow(heightFactor, {distributionExponent});
    hNorm = pow(heightFactorPow + (1.0f - heightFactorPow) * RAND, rcp({distributionExponent}));
    hNorm = (hNorm - heightFactor) / (1.0f - heightFactor); // remap on [0,1]
}}
else
    hNorm = RAND; // Uniform distribution on cylinder
";
                }
                else
                {
                    outSource += @"
float hNorm = HeightSequencer;
";
                }

                outSource += VFXBlockUtility.GetComposeString(compositionDirection, "direction.xzy", "normalize(float3(pos * sincosSlope.x, sincosSlope.y))", "blendDirection") + "\n";
                outSource += VFXBlockUtility.GetComposeString(compositionPosition, "position.xzy", "lerp(float3(pos * ArcCone_radius0, 0.0f), float3(pos * ArcCone_radius1, ArcCone_height), hNorm) + ArcCone_center.xzy", "blendPosition");
//finalPos = mul(transformMatrix, float4(finalPos, 1.0f));
//position += finalPos;
//WIP : Merge it correctly
                return outSource;
            }
        }
    }
}
