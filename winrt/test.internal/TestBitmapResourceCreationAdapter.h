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

class TestBitmapResourceCreationAdapter : public ICanvasBitmapResourceCreationAdapter
{
    ComPtr<IWICFormatConverter> m_converter;

public:
    std::function<void()> MockCreateWICFormatConverter;

    TestBitmapResourceCreationAdapter()
    {
    }

    TestBitmapResourceCreationAdapter(ComPtr<IWICFormatConverter> converter)
        : m_converter(converter)
    {
    }

    ComPtr<IWICFormatConverter> CreateWICFormatConverter(HSTRING fileName)
    {
        if (MockCreateWICFormatConverter)
            MockCreateWICFormatConverter();
        return m_converter;
    }

    ComPtr<IWICFormatConverter> CreateWICFormatConverter(IStream* fileStream)
    {
        Assert::Fail(); // Unexpected
        return nullptr;
    }
};


inline ComPtr<CanvasBitmap> CreateStubCanvasBitmap()
{
    auto adapter = std::make_shared<TestBitmapResourceCreationAdapter>();
    auto manager = std::make_shared<CanvasBitmapManager>(adapter);

    return manager->GetOrCreate(Make<StubD2DBitmap>().Get());
}
