﻿using System;
using GoodAI.ToyWorld.Control;
using OpenTK.Graphics.OpenGL;
using Render.Renderer;
using Render.RenderObjects.Buffers;
using Render.RenderObjects.Effects;
using Render.RenderObjects.Geometries;
using Render.RenderObjects.Textures;
using VRageMath;
using World.Physics;
using World.ToyWorldCore;
using Rectangle = VRageMath.Rectangle;
using RectangleF = VRageMath.RectangleF;

namespace Render.RenderRequests
{
    public abstract class RenderRequest : IRenderRequestBase, IDisposable
    {
        [Flags]
        private enum DirtyParams
        {
            None = 0,
            Size = 1,
            Resolution = 1 << 1,
            Image = 1 << 2,
            Noise = 1 << 3,
        }


        #region Fields

        private BasicFbo m_fbo;
        private NoEffectOffset m_effect;
        private NoiseEffect m_noiseEffect;
        private TilesetTexture m_tex;
        private FullScreenGrid m_grid;
        private FullScreenQuadOffset m_quadOffset;
        private FullScreenQuad m_quad;

        private Matrix m_projMatrix;
        private Matrix m_viewProjectionMatrix;

        private DirtyParams m_dirtyParams;
        private double m_simTime;

        #endregion

        #region Genesis

        protected RenderRequest()
        {
            PositionCenterV = new Vector3(0, 0, 20);
            SizeV = new Vector2(3, 3);
            Resolution = new System.Drawing.Size(1024, 1024);
            Image = new uint[0];
        }

        public virtual void Dispose()
        {
            m_fbo.Dispose();
            m_effect.Dispose();
            m_tex.Dispose();
            m_grid.Dispose();
            m_quadOffset.Dispose();
        }

        #endregion

        #region View control properties

        /// <summary>
        /// The position of the center of view.
        /// </summary>
        protected Vector3 PositionCenterV { get; set; }
        /// <summary>
        /// The position of the center of view. Equivalent to PositionCenterV (except for the z value).
        /// </summary>
        protected Vector2 PositionCenterV2 { get { return new Vector2(PositionCenterV); } set { PositionCenterV = new Vector3(value, PositionCenterV.Z); } }

        private Vector2 m_sizeV;
        protected Vector2 SizeV
        {
            get { return m_sizeV; }
            set
            {
                const float minSize = 0.01f;
                m_sizeV = new Vector2(Math.Max(minSize, value.X), Math.Max(minSize, value.Y));
                m_dirtyParams |= DirtyParams.Size;
            }
        }

        protected RectangleF ViewV { get { return new RectangleF(Vector2.Zero, SizeV) { Center = new Vector2(PositionCenterV) }; } }

        private Rectangle GridView
        {
            get
            {
                var positionOffset = new Vector2(ViewV.Width % 2, View.Height % 2); // Always use a grid with even-sized sides to have it correctly centered
                var rect = new RectangleF(Vector2.Zero, ViewV.Size + 2 + positionOffset) { Center = ViewV.Center - positionOffset };
                return new Rectangle(
                    new Vector2I(
                        (int)Math.Ceiling(rect.Position.X),
                        (int)Math.Ceiling(rect.Position.Y)),
                    (Vector2I)rect.Size);
            }
        }

        #endregion

        #region IRenderRequestBase overrides

        public System.Drawing.PointF PositionCenter
        {
            get { return new System.Drawing.PointF(PositionCenterV.X, PositionCenterV.Y); }
            protected set { PositionCenterV2 = new Vector2(value.X, value.Y); }
        }

        public virtual System.Drawing.SizeF Size
        {
            get { return new System.Drawing.SizeF(SizeV.X, SizeV.Y); }
            set { SizeV = (Vector2)value; }
        }

        public System.Drawing.RectangleF View
        {
            get { return new System.Drawing.RectangleF(PositionCenter, Size); }
        }


        private System.Drawing.Size m_resolution;
        public System.Drawing.Size Resolution
        {
            get { return m_resolution; }
            set
            {
                const int minResolution = 16;
                const int maxResolution = 4096;
                if (value.Width < minResolution || value.Height < minResolution)
                    throw new ArgumentOutOfRangeException("value", "Invalid resolution: must be greater than " + minResolution + " pixels.");
                if (value.Width > maxResolution || value.Height > maxResolution)
                    throw new ArgumentOutOfRangeException("value", "Invalid resolution: must be smaller than " + maxResolution + " pixels.");

                m_resolution = value;
                m_dirtyParams |= DirtyParams.Resolution | DirtyParams.Image;
            }
        }


        private bool m_gatherImage;
        public bool GatherImage
        {
            get { return m_gatherImage; }
            set
            {
                m_gatherImage = value;
                m_dirtyParams |= DirtyParams.Image;
            }
        }

        public uint[] Image { get; private set; }


        private bool m_drawNoise;
        private System.Drawing.Color m_noiseColor = System.Drawing.Color.FromArgb(242, 242, 242, 242);
        private float m_noiseTransformationSpeedCoefficient = 1f;
        private float m_noiseMeanOffset = 0.6f;

        public bool DrawNoise
        {
            get { return m_drawNoise; }
            set
            {
                m_drawNoise = value;
                m_dirtyParams |= DirtyParams.Noise;
            }
        }
        public System.Drawing.Color NoiseColor
        {
            get { return m_noiseColor; }
            set
            {
                m_noiseColor = value;
                m_dirtyParams |= DirtyParams.Noise;
            }
        }
        public float NoiseTransformationSpeedCoefficient
        {
            get { return m_noiseTransformationSpeedCoefficient; }
            set
            {
                m_noiseTransformationSpeedCoefficient = value;
                m_dirtyParams |= DirtyParams.Noise;
            }
        }
        public float NoiseMeanOffset
        {
            get { return m_noiseMeanOffset; }
            set
            {
                m_noiseMeanOffset = value;
                m_dirtyParams |= DirtyParams.Noise;
            }
        }

        #endregion

        #region Init

        public virtual void Init(RendererBase renderer, ToyWorld world)
        {
            // Setup color and blending
            const int baseIntensity = 50;
            GL.ClearColor(System.Drawing.Color.FromArgb(baseIntensity, baseIntensity, baseIntensity));
            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            string[] tilesetImagePaths = world.TilesetTable.GetTilesetImages();
            TilesetImage[] tilesetImages = new TilesetImage[tilesetImagePaths.Length];
            for (int i = 0; i < tilesetImages.Length; i++)
            {
                tilesetImages[i] = new TilesetImage(tilesetImagePaths[i],world.TilesetTable.TileSize, world.TilesetTable.TileMargins);
            }

            // Set up tileset textures
            m_tex = renderer.TextureManager.Get<TilesetTexture>(tilesetImages);

            // Set up the noise shader
            m_noiseEffect = renderer.EffectManager.Get<NoiseEffect>();

            // Set up tile grid shaders
            m_effect = renderer.EffectManager.Get<NoEffectOffset>();
            renderer.EffectManager.Use(m_effect); // Need to use the effect to set uniforms
            m_effect.SetUniform1(m_effect.GetUniformLocation("tex"), 0);

            // Set up static uniforms
            Vector2I fullTileSize = world.TilesetTable.TileSize + world.TilesetTable.TileMargins + new Vector2I(4,4);
            Vector2 tileCount = (Vector2)m_tex.Size / (Vector2)fullTileSize;
            m_effect.SetUniform3(m_effect.GetUniformLocation("texSizeCount"), new Vector3I(m_tex.Size.X, m_tex.Size.Y, (int)tileCount.X));
            m_effect.SetUniform4(m_effect.GetUniformLocation("tileSizeMargin"), new Vector4I(world.TilesetTable.TileSize, world.TilesetTable.TileMargins));
            m_effect.SetUniform2(m_effect.GetUniformLocation("tileBorder"), new Vector2I(2, 2));

            // Set up geometry
            m_quad = renderer.GeometryManager.Get<FullScreenQuad>();
            m_quadOffset = renderer.GeometryManager.Get<FullScreenQuadOffset>();

            CheckDirtyParams(renderer); // Do the hard work in Init
        }

        private void CheckDirtyParams(RendererBase renderer)
        {
            // Only setup these things when their dependency has changed (property setters enable these)

            if (m_dirtyParams.HasFlag(DirtyParams.Size))
            {
                m_grid = renderer.GeometryManager.Get<FullScreenGrid>(GridView.Size);
                m_projMatrix = Matrix.CreateOrthographic(SizeV.X, SizeV.Y, -1, 500);
                //m_projMatrix = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 1f, 500);
            }
            if (m_dirtyParams.HasFlag(DirtyParams.Resolution))
            {
                if (m_fbo != null)
                    m_fbo.Dispose();
                m_fbo = new BasicFbo(renderer.TextureManager, (Vector2I)Resolution);
            }
            if (m_dirtyParams.HasFlag(DirtyParams.Image))
            {
                if (!GatherImage)
                    Image = new uint[0];
                else if (Image.Length < Resolution.Width * Resolution.Height)
                    Image = new uint[Resolution.Width * Resolution.Height];
            }
            if (m_dirtyParams.HasFlag(DirtyParams.Noise))
            {
                renderer.EffectManager.Use(m_noiseEffect); // Need to use the effect to set uniforms
                m_noiseEffect.SetUniform4(
                    m_noiseEffect.GetUniformLocation("noiseColor"),
                    new Vector4(NoiseColor.R, NoiseColor.G, NoiseColor.B, NoiseColor.A) / 255f);
            }

            m_dirtyParams = DirtyParams.None;
        }

        #endregion

        #region Draw

        public virtual void Draw(RendererBase renderer, ToyWorld world)
        {
            CheckDirtyParams(renderer);

            GL.Viewport(new System.Drawing.Rectangle(0,0,Resolution.Width,Resolution.Height));

            GL.Clear(ClearBufferMask.ColorBufferBit);

            // View and proj transforms
            m_viewProjectionMatrix = GetViewMatrix(PositionCenterV);
            m_viewProjectionMatrix *= m_projMatrix;

            // Bind stuff to GL
            m_fbo.Bind();
            renderer.EffectManager.Use(m_effect);
            renderer.TextureManager.Bind(m_tex);

            // Draw the scene
            DrawTileLayers(world);
            DrawObjectLayers(world);

            // Draw effects
            DrawEffects(renderer);

            // Copy the rendered scene
            GatherAndDistributeData(renderer);
        }

        protected Matrix GetViewMatrix(Vector3 cameraPos, Vector3? cameraDirection = default(Vector3?))
        {
            if (!cameraDirection.HasValue)
                cameraDirection = Vector3.Forward;

            Matrix viewMatrix = Matrix.CreateLookAt(cameraPos, cameraPos + cameraDirection.Value, Vector3.Up);

            return viewMatrix;
        }

        private void DrawTileLayers(ToyWorld world)
        {
            // Set up transformation to screen space for tiles
            Matrix transform = Matrix.Identity;
            // Model transform -- scale from (-1,1) to viewSize/2, center on origin
            transform *= Matrix.CreateScale((Vector2)GridView.Size / 2);
            // World transform -- move center to view center
            transform *= Matrix.CreateTranslation(new Vector2(GridView.Center));
            m_effect.SetUniformMatrix4(m_effect.GetUniformLocation("mvp"), transform * m_viewProjectionMatrix);


            // Draw tile layers
            foreach (var tileLayer in world.Atlas.TileLayers)
            {
                //transform *= Matrix.CreateTranslation(0, 0, -0.1f);
                //m_effect.SetUniformMatrix4(m_mvpPos, transform * m_viewProjectionMatrix);

                m_grid.SetTextureOffsets(tileLayer.GetRectangle(GridView));
                m_grid.Draw();
            }
        }

        private void DrawObjectLayers(ToyWorld world)
        {
            // Draw objects
            foreach (var objectLayer in world.Atlas.ObjectLayers)
            {
                // TODO: Setup for this object layer

                foreach (var gameObject in objectLayer.GetGameObjects(new RectangleF(GridView)))
                {
                    // Set up transformation to screen space for the gameObject
                    Matrix transform = Matrix.Identity;
                    // Model transform
                    IDirectable dir = gameObject as IDirectable;
                    if (dir != null)
                        transform *= Matrix.CreateRotationZ(dir.Direction);
                    transform *= Matrix.CreateScale(gameObject.Size * 0.5f); // from (-1,1) to (-size,size)/2
                    // World transform
                    transform *= Matrix.CreateTranslation(new Vector3(gameObject.Position, 0.01f));
                    m_effect.SetUniformMatrix4(m_effect.GetUniformLocation("mvp"), transform * m_viewProjectionMatrix);

                    // Setup dynamic data
                    m_quadOffset.SetTextureOffsets(gameObject.TilesetId);

                    m_quadOffset.Draw();
                }
            }
        }

        private void DrawEffects(RendererBase renderer)
        {
            if (DrawNoise)
            {
                // Advance noise time by a visually pleasing step; wrap around if we run for waaaaay too long.
                m_simTime = (m_simTime + 0.005f * NoiseTransformationSpeedCoefficient) % 3e15;

                renderer.EffectManager.Use(m_noiseEffect);

                // Set up transformation to world and screen space for noise effect
                Matrix transform = Matrix.Identity;
                // Model transform -- scale from (-1,1) to viewSize/2, center on origin
                transform *= Matrix.CreateScale(ViewV.Size / 2);
                // World transform -- move center to view center
                transform *= Matrix.CreateTranslation(new Vector3(ViewV.Center, 1f));
                m_noiseEffect.SetUniformMatrix4(m_noiseEffect.GetUniformLocation("mw"), transform);
                m_noiseEffect.SetUniformMatrix4(m_noiseEffect.GetUniformLocation("mvp"), transform * m_viewProjectionMatrix);

                m_noiseEffect.SetUniform4(m_noiseEffect.GetUniformLocation("timeMean"), new Vector4((float)m_simTime, NoiseMeanOffset, 0, 0));

                m_quad.Draw();
            }

            // more stufffs
        }

        private void GatherAndDistributeData(RendererBase renderer)
        {
            // Gather data to host mem
            if (GatherImage)
            {
                GL.ReadPixels(0, 0, Resolution.Width, Resolution.Height, PixelFormat.Bgra, PixelType.UnsignedByte, Image);

                // TODO: TEMP: copy to default framebuffer (our window) -- will be removed
                m_fbo.Bind(FramebufferTarget.ReadFramebuffer);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
                GL.BlitFramebuffer(
                    0, 0, Resolution.Width, Resolution.Height,
                    0, 0, renderer.Width, renderer.Height,
                    ClearBufferMask.ColorBufferBit,
                    BlitFramebufferFilter.Linear);
            }
        }

        #endregion
    }
}
