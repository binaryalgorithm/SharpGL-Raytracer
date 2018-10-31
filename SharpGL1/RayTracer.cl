// custom struct definitions

static const int VOXEL_SIZE = 32;

typedef struct Camera
{
	float x;
	float y;
	float z;
	float hRotation;
	float vRotation;	
	float rightX;
	float rightY;
	float rightZ;
	float upX;
	float upY;
	float upZ;
	float forwardX;
	float forwardY;
	float forwardZ;
} Camera;

typedef struct ChunkData
{	
    int valid; // struct is populated
    int generated; // voxel gen occured
    int empty; // only air voxels
    int size; // cubic side length

	int chunkX; // absolute world coordinates divided by chunk size, chunk coordinates
	int chunkY;
	int chunkZ;
	int hash;

	char voxelData[VOXEL_SIZE * VOXEL_SIZE * VOXEL_SIZE];
} ChunkData;

// hash func

int Hash(int x, int y, int z, int currentArraySize)
{
    int h = 1572869;
    h ^= (h << 5) + (h >> 2) + (x * 3145739);
    h ^= (h << 5) + (h >> 2) + (y * 25165843);
    h ^= (h << 5) + (h >> 2) + (z * 2013266113);

    h = (int)((uint)h % (uint)currentArraySize);
    return h;
}

int Find(int x, int y, int z, global ChunkData* chunkHashData, int maxOffset, int currentArraySize)
{
    int hash = Hash(x, y, z, currentArraySize);
    int slotHash = hash;

    while (slotHash <= hash + maxOffset)
    {
        if (chunkHashData[slotHash].chunkX == x && chunkHashData[slotHash].chunkY == y && chunkHashData[slotHash].chunkZ == z)
        {
            // key match
            return slotHash;
        }

        slotHash++;
    }

    // not found
    return -1;
}

int ChunkFromVoxelF(float vPosf)
{
	int vPos = floor(vPosf);

	if(vPos >= 0)
	{
		return (vPos / VOXEL_SIZE); // 0-7=0 8-15=1
	}
	else
	{
		return ((vPos + 1) / VOXEL_SIZE) - 1; // -1 to -8 = -1
	}
}

int ChunkFromVoxel(int vPos)
{
	if(vPos >= 0)
	{
		return (vPos / VOXEL_SIZE); // 0-7=0 8-15=1
	}
	else
	{
		return ((vPos + 1) / VOXEL_SIZE) - 1; // -1 to -8 = -1
	}
}

int MinVoxelFromChunk(int vPos)
{
	return vPos * VOXEL_SIZE;
}

int MaxVoxelFromChunk(int vPos)
{
	return ((vPos + 1) * VOXEL_SIZE) - 1;
}

// main func

kernel void RayTraceMain(write_only image2d_t bmp, global Camera* camera, global float* textureData, global ChunkData* chunkHashData, int maxOffset, int currentArraySize, float mouseX, float mouseY)
{
	// current pixel coordinates
	int tx = get_global_id(0);
	int ty = get_global_id(1);

	// image size data (get_global_size() could work too?)
	int w = get_image_width(bmp);
	int h = get_image_height(bmp);

	// camera
    float hRot = camera[0].hRotation;
    float vRot = camera[0].vRotation;
    vRot = clamp(vRot, -90.0f, 90.0f);

    float yaw = hRot;
    float pitch = vRot;
    float cosPitch = cos(radians(pitch));
    float sinPitch = sin(radians(pitch));
    float cosYaw = cos(radians(yaw));
    float sinYaw = sin(radians(yaw));

    camera[0].rightX = cosYaw;
    camera[0].rightY = 0.0f;
    camera[0].rightZ = -sinYaw;

    camera[0].upX = sinYaw * sinPitch;
    camera[0].upY = cosPitch;
    camera[0].upZ = cosYaw * sinPitch;

    camera[0].forwardX = sinYaw * cosPitch;
    camera[0].forwardY = -sinPitch;
    camera[0].forwardZ = cosPitch * cosYaw;

    // raster coordinates (0..1, 0..1)
    float px = ( ((float)(tx) + 0.5f) / (float)w);
    float py = ( ((float)(ty) + 0.5f) / (float)h);
    float ratio = (float)w / (float)h; // should be > 1.0, normalized to Y-axis of screen (which is 1.0 here)

    float FOV = 90.0f;
    float halfFOV = FOV / 2.0f;

    // middle of screen is 0,0 in this frame
    px = (px - 0.5f) * 2; // normalize: -1...+1
    py = (py - 0.5f) * 2; // normalize: -1...+1

    float vx = px * tan(radians(halfFOV)) * ratio;
    float vy = py * tan(radians(halfFOV));
    float vz = -1.0f;

	// normalized vector to rotate
    float vlength = sqrt(vx * vx + vy * vy + vz * vz);
    float norm_starting_x = vx / vlength;
    float norm_starting_y = vy / vlength;
    float norm_starting_z = vz / vlength;
    
    // normalized rotation axis
    float x = 0.0f;
    float y = 1.0f;
    float z = 0.0f;

    float rho_deg = hRot;
    float c = cos(radians(rho_deg));
    float s = sin(radians(rho_deg));
    float t = (1 - cos(radians(rho_deg)));

    float norm_final_x = norm_starting_x * (t * x * x + c) + norm_starting_y * (t * x * y - s * z) + norm_starting_z * (t * x * z + s * y);
    float norm_final_y = norm_starting_x * (t * x * y + s * z) + norm_starting_y * (t * y * y + c) + norm_starting_z * (t * y * z - s * x);
    float norm_final_z = norm_starting_x * (t * x * z - s * y) + norm_starting_y * (t * y * z + s * x) + norm_starting_z * (t * z * z + c);

	// second phase
    norm_starting_x = norm_final_x;
    norm_starting_y = norm_final_y;
    norm_starting_z = norm_final_z;

    // rotate relative to NEW local 'right' vector
    x = camera[0].rightX;
    y = camera[0].rightY;
    z = camera[0].rightZ;

    rho_deg = vRot; // rot_angle;
    c = cos(radians(rho_deg));
    s = sin(radians(rho_deg));
    t = (1 - cos(radians(rho_deg)));

    norm_final_x = norm_starting_x * (t * x * x + c) + norm_starting_y * (t * x * y - s * z) + norm_starting_z * (t * x * z + s * y);
    norm_final_y = norm_starting_x * (t * x * y + s * z) + norm_starting_y * (t * y * y + c) + norm_starting_z * (t * y * z - s * x);
    norm_final_z = norm_starting_x * (t * x * z - s * y) + norm_starting_y * (t * y * z + s * x) + norm_starting_z * (t * z * z + c);

    vx = norm_final_x;
    vy = norm_final_y;
    vz = norm_final_z;

	// init ray at camera position (eye position)
    float rayx = camera[0].x;
    float rayy = camera[0].y;
    float rayz = camera[0].z;

	int camChunkX = ChunkFromVoxelF(rayx);
	int camChunkY = ChunkFromVoxelF(rayy);
	int camChunkZ = ChunkFromVoxelF(rayz);

	// init pixel color
    float red = 0.0f;
    float green = 0.0f;
    float blue = 0.0f;

	// max ray travel distance
    float maxDistance = 256.0f; // safety measure
    maxDistance = 96.0f;
	float currentDistance = 0.0f;

	float ix = 0.0f;
    float iy = 0.0f;
    float iz = 0.0f;

	int voxelX = (int)floor(rayx);
	int voxelY = (int)floor(rayy);
	int voxelZ = (int)floor(rayz);

	int chunkX = 0;
    int chunkY = 0;
    int chunkZ = 0;

    // ray loop

	int cycleCount = 0;
	int notFoundCount = 0;
	int emptyCount = 0;

    while (currentDistance < maxDistance)
    {
		cycleCount++;

        voxelX = (int)floor(rayx);
        voxelY = (int)floor(rayy);
        voxelZ = (int)floor(rayz);

		chunkX = ChunkFromVoxel(voxelX);
		chunkY = ChunkFromVoxel(voxelY);
		chunkZ = ChunkFromVoxel(voxelZ);

		int chunkOffsetX = (voxelX & (VOXEL_SIZE - 1));
		int chunkOffsetY = (voxelY & (VOXEL_SIZE - 1));
		int chunkOffsetZ = (voxelZ & (VOXEL_SIZE - 1));

		if(abs(camChunkX - chunkX) > 3 || abs(camChunkY - chunkY) > 3 || abs(camChunkZ - chunkZ) > 3)
		{
			break;
		}

		int vType = 0;
		int emptyFlag = 1;

		int index = Find(chunkX, chunkY, chunkZ, chunkHashData, maxOffset, currentArraySize);		

		if(index >= 0)
		{
			global ChunkData* cdata = &chunkHashData[index];
			int subIndex = chunkOffsetX + (chunkOffsetY * VOXEL_SIZE) + (chunkOffsetZ * VOXEL_SIZE * VOXEL_SIZE);
			vType = cdata->voxelData[subIndex];
			emptyFlag = cdata->empty;
		}
		else
		{
			notFoundCount++;
		}

		if(vType > 0)
		{
			ix = rayx - floor(rayx);
			iy = rayy - floor(rayy);
			iz = rayz - floor(rayz);

			// get dist remaining in cube axis, reverse direction of ray
			if (vx < 0) { ix = 1.0f - ix; }
			if (vy < 0) { iy = 1.0f - iy; }
			if (vz < 0) { iz = 1.0f - iz; }

			ix = fabs(ix / vx);
			iy = fabs(iy / vy);
			iz = fabs(iz / vz);

			float nextDistance = min(iz, min(ix, iy)) - 0.001f;

			rayx += vx * nextDistance;
			rayy += vy * nextDistance;
			rayz += vz * nextDistance;

			ix = rayx - floor(rayx);
			iy = rayy - floor(rayy);
			iz = rayz - floor(rayz);

			// now we should be at the edge of the cube in one axis, and 0...1] in the other 2

			int face = 0;

			float vMin = 1.0f;

			if(ix < vMin) { vMin = ix; face = 1; }
			if((1.0f - ix) < vMin) { vMin = (1.0f - ix); face = 2; }

			if(iy < vMin) { vMin = iy; face = 3; }
			if((1.0f - iy) < vMin) { vMin = (1.0f - iy); face = 4; }

			if(iz < vMin) { vMin = iz; face = 5; }
			if((1.0f - iz) < vMin) { vMin = (1.0f - iz); face = 6; }

			float textureSizeF = 32.0f;
			int textureSize = 32;

			int textureX = vType % 16;
			int textureY = vType / 16;

			// texture (x,y) => 0...15

			int u = textureX * textureSize;
			int v = textureY * textureSize;

			if(face == 1)
			{
				u += clamp((int)((1 - iz) * textureSizeF), 0, textureSize);
				v += clamp((int)(iy * textureSizeF), 0, textureSize);
			}
			else if(face == 2)
			{
				u += clamp((int)(iz * textureSizeF), 0, textureSize);
				v += clamp((int)(iy * textureSizeF), 0, textureSize);
			}
			else if(face == 3)
			{
				u += clamp((int)(ix * textureSizeF), 0, textureSize);
				v += clamp((int)((1 - iz) * textureSizeF), 0, textureSize);
			}
			else if(face == 4)
			{
				u += clamp((int)(ix * textureSizeF), 0, textureSize);
				v += clamp((int)(iz * textureSizeF), 0, textureSize);
			}
			else if(face == 5)
			{
				u += clamp((int)(ix * textureSizeF), 0, textureSize);
				v += clamp((int)(iy * textureSizeF), 0, textureSize);
			}
			else if(face == 6)
			{
				u += clamp((int)((1 - ix) * textureSizeF), 0, textureSize);
				v += clamp((int)(iy * textureSizeF), 0, textureSize);
			}			

			blue = textureData[(u + (v * textureSize * 16)) * 3 + 0];
			green = textureData[(u + (v * textureSize * 16)) * 3 + 1];
			red = textureData[(u + (v * textureSize * 16)) * 3 + 2];

			if(voxelX == (int)camera[1].x && voxelY == (int)camera[1].y && voxelZ == (int)camera[1].z)
			{
				red = 1.0f; // highlight voxel of mouse hit
			}

			// record face of mouse hit

			if(tx == (int)(mouseX * w) && ty == (int)(mouseY * h))
			{
				camera[2].forwardX = face;
			}

			break;
		}
		
		if(emptyFlag == 1) // skip VOXEL_SIZE voxels in this large cube (chunk), as it's empty
		{
			emptyCount++;

			// x/y/z of cube volume
			float x1 = MinVoxelFromChunk(chunkX);
			float x2 = MaxVoxelFromChunk(chunkX) + 1;
			float y1 = MinVoxelFromChunk(chunkY);
			float y2 = MaxVoxelFromChunk(chunkY) + 1;
			float z1 = MinVoxelFromChunk(chunkZ);
			float z2 = MaxVoxelFromChunk(chunkZ) + 1;

			// get dist remaining in cube axis
			if (vx >= 0) { ix = x2 - rayx; } else { ix = rayx - x1; }
			if (vy >= 0) { iy = y2 - rayy; } else { iy = rayy - y1; }
			if (vz >= 0) { iz = z2 - rayz; } else { iz = rayz - z1; }

			ix = fabs(ix / vx);
			iy = fabs(iy / vy);
			iz = fabs(iz / vz);

			float nextDistance = min(iz, min(ix, iy)) + 0.01f; // step just over boundary

			rayx += vx * nextDistance;
			rayy += vy * nextDistance;
			rayz += vz * nextDistance;

			currentDistance += nextDistance; // add step length
		}
		else // single voxel step
		{
			// x/y/z of cube volume
			float x1 = floor(rayx);
			float x2 = x1 + 1.0f;
			float y1 = floor(rayy);
			float y2 = y1 + 1.0f;
			float z1 = floor(rayz);
			float z2 = z1 + 1.0f;

			// get dist remaining in cube axis
			if (vx >= 0) { ix = x2 - rayx; } else { ix = rayx - x1; }
			if (vy >= 0) { iy = y2 - rayy; } else { iy = rayy - y1; }
			if (vz >= 0) { iz = z2 - rayz; } else { iz = rayz - z1; }

			ix = fabs(ix / vx);
			iy = fabs(iy / vy);
			iz = fabs(iz / vz);

			float nextDistance = min(iz, min(ix, iy)) + 0.01f; // step just over boundary

			rayx += vx * nextDistance;
			rayy += vy * nextDistance;
			rayz += vz * nextDistance;

			currentDistance += nextDistance; // add step length
		}
    }

	if(tx == (int)(mouseX * w) || ty == (int)(mouseY * h))
	{
		// crosshair test
		//red = 1.0f;
		//green = 0.0f;
		//blue = 0.0f;
	}

	if(tx == (int)(mouseX * w) && ty == (int)(mouseY * h))
	{
		camera[2].x = voxelX;
		camera[2].y = voxelY;
		camera[2].z = voxelZ;
	}

	red += notFoundCount / 16.0f;
	//green += emptyCount / 32.0f;
	blue += cycleCount / 64.0f;
	
	// write pixel value to buffer

	int2 coords = (int2)((int)tx, (int)ty);
	float4 val = (float4)(red, green, blue, 1.0f);
    write_imagef(bmp, coords, val);  

	return;
}
