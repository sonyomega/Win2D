// Copyright (c) Microsoft Corporation. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may
// not use these files except in compliance with the License. You may obtain
// a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
// License for the specific language governing permissions and limitations
// under the License.

#include "pch.h"

#include "CanvasBitmap.h"
#include "CanvasDevice.h"
#include "CanvasDrawingSession.h"
#include "CanvasRenderTarget.h"

namespace ABI { namespace Microsoft { namespace Graphics { namespace Canvas
{
    using namespace ABI::Windows::Storage::Streams;
    using namespace ::Microsoft::WRL::Wrappers;

    //
    // CanvasBitmapManager
    //

    CanvasBitmapManager::CanvasBitmapManager(std::shared_ptr<ICanvasBitmapResourceCreationAdapter> adapter)
        : m_adapter(adapter)
    {
    }


    ComPtr<CanvasBitmap> CanvasBitmapManager::CreateNew(
        ICanvasDevice* canvasDevice,
        HSTRING fileName,
        CanvasAlphaBehavior alpha)
    {
        ComPtr<ICanvasDeviceInternal> canvasDeviceInternal;
        ThrowIfFailed(canvasDevice->QueryInterface(canvasDeviceInternal.GetAddressOf()));

        auto d2dBitmap = canvasDeviceInternal->CreateBitmapFromWicResource(m_adapter->CreateWICFormatConverter(fileName).Get(), alpha);

        auto bitmap = Make<CanvasBitmap>(
            shared_from_this(),
            d2dBitmap.Get());
        CheckMakeResult(bitmap);
        
        return bitmap;
    }


    ComPtr<CanvasBitmap> CanvasBitmapManager::CreateNew(
        ICanvasDevice* canvasDevice,
        IStream* fileStream,
        CanvasAlphaBehavior alpha)
    {
        ComPtr<ICanvasDeviceInternal> canvasDeviceInternal;
        ThrowIfFailed(canvasDevice->QueryInterface(canvasDeviceInternal.GetAddressOf()));

        auto d2dBitmap = canvasDeviceInternal->CreateBitmapFromWicResource(m_adapter->CreateWICFormatConverter(fileStream).Get(), alpha);

        auto bitmap = Make<CanvasBitmap>(
            shared_from_this(),
            d2dBitmap.Get());
        CheckMakeResult(bitmap);
        
        return bitmap;
    }


    ComPtr<CanvasBitmap> CanvasBitmapManager::CreateNew(
        ICanvasDevice* device,
        uint32_t byteCount,
        BYTE* bytes,
        int32_t widthInPixels,
        int32_t heightInPixels,
        DirectXPixelFormat format,
        CanvasAlphaBehavior alpha,
        float dpi)
    {
        auto d2dDevice = As<ICanvasDeviceInternal>(device)->GetD2DDevice();

        ComPtr<ID2D1DeviceContext1> deviceContext;
        ThrowIfFailed(d2dDevice->CreateDeviceContext(
            D2D1_DEVICE_CONTEXT_OPTIONS_NONE,
            &deviceContext));

        D2D1_BITMAP_PROPERTIES1 bitmapProperties = D2D1::BitmapProperties1();
        bitmapProperties.pixelFormat.alphaMode = ToD2DAlphaMode(alpha);
        bitmapProperties.pixelFormat.format = static_cast<DXGI_FORMAT>(format);
        bitmapProperties.dpiX = dpi;
        bitmapProperties.dpiY = dpi;

        // D2D does not fail attempts to create zero-sized bitmaps. Neither does this.
        uint32_t pitch = 0;
        if (heightInPixels > 0)
        {
            pitch = byteCount / static_cast<uint32_t>(heightInPixels);
        }
        else
        {
            pitch = byteCount;
        }

        ComPtr<ID2D1Bitmap1> d2dBitmap;
        ThrowIfFailed(deviceContext->CreateBitmap(D2D1::SizeU(widthInPixels, heightInPixels), bytes, pitch, &bitmapProperties, &d2dBitmap));

        auto bitmap = Make<CanvasBitmap>(
            shared_from_this(),
            d2dBitmap.Get());
        CheckMakeResult(bitmap);

        return bitmap;
    }


    ComPtr<CanvasBitmap> CanvasBitmapManager::CreateNew(
        ICanvasDevice* device,
        uint32_t colorCount,
        Color* colors,
        int32_t widthInPixels,
        int32_t heightInPixels,
        CanvasAlphaBehavior alpha,
        float dpi)
    {
        // Convert color array to bytes according to the default format, B8G8R8A8_UNORM.
        std::vector<uint8_t> convertedBytes;
        convertedBytes.resize(colorCount * 4);

        for (uint32_t i = 0; i < colorCount; i++)
        {
            convertedBytes[i * 4 + 0] = colors[i].B;
            convertedBytes[i * 4 + 1] = colors[i].G;
            convertedBytes[i * 4 + 2] = colors[i].R;
            convertedBytes[i * 4 + 3] = colors[i].A;
        }

        assert(convertedBytes.size() <= UINT_MAX);

        return CreateNew(
            device,
            static_cast<uint32_t>(convertedBytes.size()),
            convertedBytes.empty() ? nullptr : &convertedBytes[0],
            widthInPixels,
            heightInPixels,
            DirectXPixelFormat::B8G8R8A8UIntNormalized,
            alpha,
            dpi);
    }


    ComPtr<CanvasBitmap> CanvasBitmapManager::CreateWrapper(
        ID2D1Bitmap1* d2dBitmap)
    {
        auto bitmap = Make<CanvasBitmap>(
            shared_from_this(),
            d2dBitmap);
        CheckMakeResult(bitmap);

        return bitmap;
    }

    class DefaultCanvasBitmapAdapter : public ICanvasBitmapAdapter
    {
        ComPtr<IRandomAccessStreamReferenceStatics> m_randomAccessStreamReferenceStatics;
    public:
        DefaultCanvasBitmapAdapter()
        {
            ThrowIfFailed(GetActivationFactory(HStringReference(RuntimeClass_Windows_Storage_Streams_RandomAccessStreamReference).Get(), &m_randomAccessStreamReferenceStatics));
        }

        ComPtr<IRandomAccessStreamReference> CreateRandomAccessStreamFromUri(ComPtr<IUriRuntimeClass> const& uri) override
        {
            ComPtr<IRandomAccessStreamReference> randomAccessStreamReference;
            ThrowIfFailed(m_randomAccessStreamReferenceStatics->CreateFromUri(uri.Get(), &randomAccessStreamReference));

            return randomAccessStreamReference;
        }
    };

    
    //
    // CanvasBitmapFactory
    //


    CanvasBitmapFactory::CanvasBitmapFactory()
        : m_adapter(std::make_shared<DefaultCanvasBitmapAdapter>())
    {
    }

    //
    // ICanvasBitmapStatics
    //

    IFACEMETHODIMP CanvasBitmapFactory::CreateFromDirect3D11Surface(
        ICanvasResourceCreator* resourceCreator,
        IDirect3DSurface* surface,
        ICanvasBitmap** canvasBitmap)
    {
        return CreateFromDirect3D11SurfaceWithAlphaAndDpi(
            resourceCreator,
            surface,
            CanvasAlphaBehavior::Premultiplied,
            DEFAULT_DPI,
            canvasBitmap);
    }

    IFACEMETHODIMP CanvasBitmapFactory::CreateFromDirect3D11SurfaceWithAlpha(
        ICanvasResourceCreator* resourceCreator,
        IDirect3DSurface* surface,
        CanvasAlphaBehavior alpha,
        ICanvasBitmap** canvasBitmap)
    {
        return CreateFromDirect3D11SurfaceWithAlphaAndDpi(
            resourceCreator,
            surface,
            alpha,
            DEFAULT_DPI,
            canvasBitmap);
    }

    IFACEMETHODIMP CanvasBitmapFactory::CreateFromDirect3D11SurfaceWithDpi(
        ICanvasResourceCreator* resourceCreator,
        IDirect3DSurface* surface,
        float dpi,
        ICanvasBitmap** canvasBitmap)
    {
        return CreateFromDirect3D11SurfaceWithAlphaAndDpi(
            resourceCreator,
            surface,
            CanvasAlphaBehavior::Premultiplied,
            dpi,
            canvasBitmap);
    }

    IFACEMETHODIMP CanvasBitmapFactory::CreateFromDirect3D11SurfaceWithAlphaAndDpi(
        ICanvasResourceCreator* resourceCreator,
        IDirect3DSurface* surface,
        CanvasAlphaBehavior alpha,
        float dpi,
        ICanvasBitmap** canvasBitmap)
    {
        return ExceptionBoundary(
            [&]
            {
                CheckInPointer(resourceCreator);
                CheckInPointer(surface);
                CheckAndClearOutPointer(canvasBitmap);

                ComPtr<ICanvasDevice> canvasDevice;
                ThrowIfFailed(resourceCreator->get_Device(&canvasDevice));

                auto newBitmap = GetManager()->CreateBitmapFromSurface(canvasDevice.Get(), surface, alpha, dpi);
                ThrowIfFailed(newBitmap.CopyTo(canvasBitmap));
            });
    }

    IFACEMETHODIMP CanvasBitmapFactory::CreateFromBytes(
        ICanvasResourceCreator* resourceCreator,
        uint32_t byteCount,
        BYTE* bytes,
        int32_t widthInPixels,
        int32_t heightInPixels,
        DirectXPixelFormat format,
        CanvasAlphaBehavior alpha,
        ICanvasBitmap** canvasBitmap)
    {
        return CreateFromBytesWithDpi(
            resourceCreator,
            byteCount,
            bytes,
            widthInPixels,
            heightInPixels,
            format,
            alpha,
            DEFAULT_DPI,
            canvasBitmap);
    }

    IFACEMETHODIMP CanvasBitmapFactory::CreateFromBytesWithDpi(
        ICanvasResourceCreator* resourceCreator,
        uint32_t byteCount,
        BYTE* bytes,
        int32_t widthInPixels,
        int32_t heightInPixels,
        DirectXPixelFormat format,
        CanvasAlphaBehavior alpha,
        float dpi,
        ICanvasBitmap** canvasBitmap)
    {
        return ExceptionBoundary(
            [&]
            {
                CheckInPointer(resourceCreator);
                if (byteCount)
                    CheckInPointer(bytes);
                CheckAndClearOutPointer(canvasBitmap);

                ComPtr<ICanvasDevice> canvasDevice;
                ThrowIfFailed(resourceCreator->get_Device(&canvasDevice));

                auto newBitmap = GetManager()->CreateBitmap(
                    canvasDevice.Get(), 
                    byteCount, 
                    bytes, 
                    widthInPixels,
                    heightInPixels,
                    format, 
                    alpha,
                    dpi);

                ThrowIfFailed(newBitmap.CopyTo(canvasBitmap));
            });
    }

    IFACEMETHODIMP CanvasBitmapFactory::CreateFromColors(
        ICanvasResourceCreator* resourceCreator,
        uint32_t colorCount,
        ABI::Windows::UI::Color* colors,
        int32_t widthInPixels,
        int32_t heightInPixels,
        CanvasAlphaBehavior alpha,
        ICanvasBitmap** canvasBitmap)
    {
        return CreateFromColorsWithDpi(
            resourceCreator,
            colorCount,
            colors,
            widthInPixels,
            heightInPixels,
            alpha,
            DEFAULT_DPI,
            canvasBitmap);
    }

    IFACEMETHODIMP CanvasBitmapFactory::CreateFromColorsWithDpi(
        ICanvasResourceCreator* resourceCreator,
        uint32_t colorCount,
        ABI::Windows::UI::Color* colors,
        int32_t widthInPixels,
        int32_t heightInPixels,
        CanvasAlphaBehavior alpha,
        float dpi,
        ICanvasBitmap** canvasBitmap)
    {
        return ExceptionBoundary(
            [&]
            {
                CheckInPointer(resourceCreator);
                if (colorCount)
                    CheckInPointer(colors);
                CheckAndClearOutPointer(canvasBitmap);

                ComPtr<ICanvasDevice> canvasDevice;
                ThrowIfFailed(resourceCreator->get_Device(&canvasDevice));

                auto newBitmap = GetManager()->CreateBitmap(
                    canvasDevice.Get(), 
                    colorCount,
                    colors,
                    widthInPixels,
                    heightInPixels,
                    alpha,
                    dpi);

                ThrowIfFailed(newBitmap.CopyTo(canvasBitmap));
            });
    }

    IFACEMETHODIMP CanvasBitmapFactory::LoadAsyncFromHstring(
        ICanvasResourceCreator* resourceCreator,
        HSTRING fileName,
        ABI::Windows::Foundation::IAsyncOperation<CanvasBitmap*>** canvasBitmapAsyncOperation)
    {
        return LoadAsyncFromHstringWithAlpha(
            resourceCreator,
            fileName,
            CanvasAlphaBehavior::Premultiplied,
            canvasBitmapAsyncOperation);
    }

    IFACEMETHODIMP CanvasBitmapFactory::LoadAsyncFromHstringWithAlpha(
        ICanvasResourceCreator* resourceCreator,
        HSTRING fileName,
        CanvasAlphaBehavior alpha,
        ABI::Windows::Foundation::IAsyncOperation<CanvasBitmap*>** canvasBitmapAsyncOperation)
    {
        return ExceptionBoundary(
            [&]
            {
                CheckInPointer(resourceCreator);
                CheckInPointer(fileName);
                CheckAndClearOutPointer(canvasBitmapAsyncOperation);

                ComPtr<ICanvasDevice> canvasDevice;
                ThrowIfFailed(resourceCreator->get_Device(&canvasDevice));

                WinString fileName(fileName);

                auto asyncOperation = Make<AsyncOperation<CanvasBitmap>>(
                    [=]
                    {
                        auto bitmap = GetManager()->CreateBitmap(canvasDevice.Get(), fileName, alpha);
                        return bitmap;
                    });

                CheckMakeResult(asyncOperation);
                ThrowIfFailed(asyncOperation.CopyTo(canvasBitmapAsyncOperation));
            });
    }

    IFACEMETHODIMP CanvasBitmapFactory::LoadAsyncFromUri(
        ICanvasResourceCreator* resourceCreator,
        ABI::Windows::Foundation::IUriRuntimeClass* uri,
        ABI::Windows::Foundation::IAsyncOperation<CanvasBitmap*>** canvasBitmapAsyncOperation)
    {
        return LoadAsyncFromUriWithAlpha(
            resourceCreator, 
            uri, 
            CanvasAlphaBehavior::Premultiplied,
            canvasBitmapAsyncOperation);
    }

    template<typename T>
    HRESULT WaitForOperation(IAsyncOperation<T*>* asyncOperation, T** ret)
    {
        // TODO #2617:Investigate making PPL work with async tasks.

        Event emptyEvent(CreateEventEx(NULL, NULL, CREATE_EVENT_MANUAL_RESET, EVENT_ALL_ACCESS));
        if (!emptyEvent.IsValid())
            return E_OUTOFMEMORY;

        ComPtr<T> taskResult;
        HRESULT taskHr = S_OK;
           
        auto callback = Callback<IAsyncOperationCompletedHandler<T*>>(
            [&emptyEvent, &taskResult, &taskHr](IAsyncOperation<T*>* asyncInfo, AsyncStatus status) -> HRESULT
            {
                taskHr = asyncInfo->GetResults(taskResult.GetAddressOf());
                SetEvent(emptyEvent.Get());
                return S_OK;
            });
        asyncOperation->put_Completed(callback.Get());

        auto timeout = 1000 * 5;
        auto waitResult = WaitForSingleObjectEx(emptyEvent.Get(), timeout, true);

        if (waitResult != WAIT_OBJECT_0)
        {
            return E_INVALIDARG;
        }

        taskResult.CopyTo(ret);

        return taskHr;
    };

    IFACEMETHODIMP CanvasBitmapFactory::LoadAsyncFromUriWithAlpha(
        ICanvasResourceCreator* resourceCreator,
        ABI::Windows::Foundation::IUriRuntimeClass* rawUri,
        CanvasAlphaBehavior alpha,
        ABI::Windows::Foundation::IAsyncOperation<CanvasBitmap*>** canvasBitmapAsyncOperation)
    {

        return ExceptionBoundary(
            [&]
            {
                CheckInPointer(resourceCreator);
                CheckInPointer(rawUri);
                CheckAndClearOutPointer(canvasBitmapAsyncOperation);

                ComPtr<ICanvasDevice> canvasDevice;
                ThrowIfFailed(resourceCreator->get_Device(&canvasDevice));

                ComPtr<IRandomAccessStreamReferenceStatics> streamRefStatics;
                ThrowIfFailed(GetActivationFactory(HStringReference(RuntimeClass_Windows_Storage_Streams_RandomAccessStreamReference).Get(), &streamRefStatics));

                ComPtr<IUriRuntimeClass> uri = rawUri;
                
                auto asyncOperation = Make<AsyncOperation<CanvasBitmap>>(
                    [=]
                    {
                        ComPtr<IRandomAccessStreamReference> randomAccessStreamReference = m_adapter->CreateRandomAccessStreamFromUri(uri);

                        ComPtr<IAsyncOperation<IRandomAccessStreamWithContentType*>> randomAccessStreamWithContentTypeOperation;
                        ThrowIfFailed(randomAccessStreamReference->OpenReadAsync(&randomAccessStreamWithContentTypeOperation));

                        ComPtr<IRandomAccessStreamWithContentType> randomAccessStream;                         
                        ThrowIfFailed(WaitForOperation<IRandomAccessStreamWithContentType>(randomAccessStreamWithContentTypeOperation.Get(), &randomAccessStream));

                        ComPtr<IStream> stream;
                        ThrowIfFailed(CreateStreamOverRandomAccessStream(randomAccessStream.Get(), IID_PPV_ARGS(&stream)));

                        auto bitmap = GetManager()->CreateBitmap(canvasDevice.Get(), stream.Get(), alpha);
                        return bitmap;
                    });

                CheckMakeResult(asyncOperation);
                ThrowIfFailed(asyncOperation.CopyTo(canvasBitmapAsyncOperation));
            });
    }

    //
    // ICanvasFactoryNative
    //


    IFACEMETHODIMP CanvasBitmapFactory::GetOrCreate(
        ICanvasDevice* device,
        IUnknown* resource,
        IInspectable** wrapper)
    {
        return ExceptionBoundary(
            [&]
            {
                CheckInPointer(resource);
                CheckAndClearOutPointer(wrapper);

                ComPtr<ID2D1Bitmap1> d2dBitmap;
                ThrowIfFailed(resource->QueryInterface(d2dBitmap.GetAddressOf()));

                auto newCanvasBitmap = GetManager()->GetOrCreateBitmap(device, d2dBitmap.Get());

                ThrowIfFailed(newCanvasBitmap.CopyTo(wrapper));
            });
    }


    //
    // CanvasBitmap
    //


    CanvasBitmap::CanvasBitmap(
        std::shared_ptr<CanvasBitmapManager> manager,
        ID2D1Bitmap1* d2dBitmap)
        : CanvasBitmapImpl(manager, d2dBitmap)
    {
        assert(!IsRenderTargetBitmap(d2dBitmap) 
            && "CanvasBitmap should never be constructed with a render-target bitmap.  This should have been validated before construction.");
    }


    ActivatableClassWithFactory(CanvasBitmap, CanvasBitmapFactory);
}}}}
