namespace OpenTPW;

/// <summary>
/// The relief shading arithmetic of tp.exe 0x5EB8A0, run instruction for instruction on emulated x87 registers
/// (<see cref="Ext"/>) at the precision control of the original's thread (<see cref="X87Precision"/>): every temporary the
/// original keeps on the FPU stack stays at register width, every FST/FSTP to a float rounds to a float.
/// </summary>
internal sealed class SignRelief
{
	private static readonly Ext Inverse255 = Ext.FromFloat( 0.003921568859368563f );
	private static readonly Ext Half = Ext.FromFloat( 0.5f );
	private static readonly Ext Scale255 = Ext.FromFloat( 255f );

	private readonly Ext normalScale;
	private readonly Ext baseCoefficient;
	private readonly Ext diffuseCoefficient;
	private readonly Ext whiteCoefficient;
	private readonly Ext exponent;
	private readonly Ext lx, ly, lz, halfX, halfY, halfZ;

	/// <summary>The light and half vectors of an effect (0x5EB920..0x5EBA70) and its coefficients.</summary>
	// [BIN:TPW-EXE:0x005EB920 light] theta = p6 * -0.0174532924f and phi = p7 * 0.0174532924f (FMUL float, rounded at the precision control); the light is (cos(phi) cos(theta_f), -(sin(phi) cos(theta_f)), -sin(theta_f)) where cos(theta_f) is the float stored at [esp+0x44] and sin(theta_f) takes the float-stored theta; the length is (ay² + az²) + ax² (FADDP order) under FSQRT and FDIVR 1.0; each component is s * a (register width) stored as a float; the half vector is hy = s*ay*0.5 (register width), hz = (lz + 1) * 0.5, hx = lx * 0.5, each squared against its own float store, summed hy²+hz² then + hx², and scaled by the float-stored reciprocal of the length (halfX takes the register-width reciprocal, halfY and halfZ the float one)
	public SignRelief( IReadOnlyList<float> parameters )
	{
		var theta = Ext.FromFloat( parameters[6] ) * Ext.FromFloat( -0.01745329238474369f );
		var phi = Ext.FromFloat( parameters[7] ) * Ext.FromFloat( 0.01745329238474369f );
		var thetaStored = theta.AsFloat();
		var cosTheta = Ext.Cos( theta );
		var sinPhi = Ext.Sin( phi );
		var sinTheta = Ext.Sin( thetaStored );
		var cosPhi = Ext.Cos( phi );
		var cosThetaStored = cosTheta.AsFloat();
		var ay = (sinPhi * cosThetaStored).Negate();
		var ax = cosPhi * cosThetaStored;
		var az = sinTheta.Negate();
		var length = Ext.Sqrt( (ay * ay + az * az) + ax * ax );
		var scale = Ext.One / length;
		var lightX = scale * ax;
		var lightY = scale * ay;
		var lightZ = scale * az;
		lx = lightX.AsFloat();
		ly = lightY.AsFloat();
		lz = lightZ.AsFloat();
		var hy = lightY * Half;
		var hz = (lz + Ext.One) * Half;
		var hx = lx * Half;
		var hyStored = hy.AsFloat();
		var hzStored = hz.AsFloat();
		var hxStored = hx.AsFloat();
		var sum = (hz * hzStored + hy * hyStored) + hx * hxStored;
		var halfScale = Ext.One / Ext.Sqrt( sum );
		var halfScaleStored = halfScale.AsFloat();
		halfX = (halfScale * hxStored).AsFloat();
		halfY = (halfScaleStored * hyStored).AsFloat();
		halfZ = (halfScaleStored * hzStored).AsFloat();
		normalScale = Ext.FromFloat( parameters[0] );
		baseCoefficient = Ext.FromFloat( parameters[2] );
		diffuseCoefficient = Ext.FromFloat( parameters[3] );
		whiteCoefficient = Ext.FromFloat( parameters[4] );
		exponent = Ext.FromFloat( parameters[5] );
	}

	/// <summary>
	/// The three colour bytes of one pixel (0x5EBAC2..0x5EBDA0): the normal from the integer shape differences, the diffuse and
	/// specular dots, the base term, the diffuse and specular additions (each stored as a float), the product with 255, the clamp
	/// 0x5EBE50 and the truncation.
	/// </summary>
	// [BIN:TPW-EXE:0x005EB8A0 relief pixel] n = (dx, dy, 1) with dx = (int * p0) * 0.003921568859368563f; the scale is 1 / sqrt((dx² + dy²) - -1.0) stored as a float and multiplied back into dx and dy (each stored as a float), nz the stored scale; diffuse dot = (nx*lx + ny*ly) + nz*lz and specular dot = (nx*hX + ny*hY) + nz*hZ, each stored as a float; the strip bytes become floats byte * 0.003921568859368563f; the base vector is float(unit * p2), a diffuse dot above 0 adds float(float(dot * p3) * unit) as float(sum), a specular dot above 0 adds float(pow(dot, p5) * p4) the same way; each channel is float(sum * 255f) clamped by 0x5EBE50 (NaN takes the lower bound) and truncated by _ftol
	public (byte R, byte G, byte B) Shade( int differenceX, int differenceY, byte strip1, byte strip2, byte strip3 )
	{
		var dx = Ext.FromInt( differenceX ) * normalScale * Inverse255;
		var dy = Ext.FromInt( differenceY ) * normalScale * Inverse255;
		var scale = (Ext.One / Ext.Sqrt( (dx * dx + dy * dy) + Ext.One )).AsFloat();
		var ny = (scale * dy).AsFloat();
		var nx = (scale * dx).AsFloat();
		var nz = scale;
		var diffuseDot = ((nx * lx + ny * ly) + nz * lz).AsFloat();
		var specularDot = ((nx * halfX + ny * halfY) + nz * halfZ).AsFloat();
		var unit0 = (Ext.FromInt( strip1 ) * Inverse255).AsFloat();
		var unit1 = (Ext.FromInt( strip2 ) * Inverse255).AsFloat();
		var unit2 = (Ext.FromInt( strip3 ) * Inverse255).AsFloat();
		var sum0 = (unit0 * baseCoefficient).AsFloat();
		var sum1 = (unit1 * baseCoefficient).AsFloat();
		var sum2 = (unit2 * baseCoefficient).AsFloat();
		if ( diffuseDot.IsPositive )
		{
			var diffuse = diffuseDot * diffuseCoefficient;
			sum0 = ((diffuse * unit0).AsFloat() + sum0).AsFloat();
			sum1 = ((diffuse * unit1).AsFloat() + sum1).AsFloat();
			sum2 = ((diffuse * unit2).AsFloat() + sum2).AsFloat();
		}
		if ( specularDot.IsPositive )
		{
			var specular = (Ext.Pow( specularDot, exponent ) * whiteCoefficient).AsFloat();
			sum0 = (specular + sum0).AsFloat();
			sum1 = (specular + sum1).AsFloat();
			sum2 = (specular + sum2).AsFloat();
		}
		return (Channel( sum0 ), Channel( sum1 ), Channel( sum2 ));
	}

	private static byte Channel( Ext sum )
	{
		var scaled = (sum * Scale255).AsFloat();
		if ( scaled.CompareTo( Scale255 ) > 0 )
			return 255;
		if ( scaled.CompareTo( Ext.Zero ) < 0 )
			return 0;
		return unchecked( (byte)scaled.Truncate() );
	}
}
