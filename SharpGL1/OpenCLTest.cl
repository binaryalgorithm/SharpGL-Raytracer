kernel void OpenCLTest(write_only image2d_t bmp, float green)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int w = get_global_size(0) - 1;
    int h = get_global_size(1) - 1;
   
    // if(x > w || y > h) { return; }

    int2 coords = (int2)(x,y);
   
    float red = (float)x/(float)w;
    float blue = (float)y/(float)h;

    float4 val = (float4)(red, green, blue, 1.0f);

    // float4 val = (float4)(1.0f, 0.0f, 1.0f, 1.0f);

    write_imagef(bmp, coords, val);  
}
