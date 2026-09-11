#include "dcmtk/config/osconfig.h"
#include "dcmtk/dcmdata/dctk.h"
#include "dcmtk/dcmdata/dcuid.h"

#include <algorithm>
#include <array>
#include <cerrno>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <memory>
#include <new>
#include <stdexcept>
#include <string>
#include <vector>

#if defined(__GNUC__)
#define DENTAL_EXPORT __attribute__((visibility("default")))
#else
#define DENTAL_EXPORT
#endif

namespace
{
constexpr const char* kRequiredDcmtkVersion = "3.6.8";

enum class IntegerField : int
{
    Rows = 1,
    Columns = 2,
    FrameCount = 3,
    IsMonochrome1 = 4,
};

enum class DoubleField : int
{
    ImagePositionPatient = 1,
    ImageOrientationPatient = 2,
    PixelSpacing = 3,
    SliceSpacing = 4,
    RescaleSlope = 5,
    RescaleIntercept = 6,
    WindowCenter = 7,
    WindowWidth = 8,
};

enum class StringField : int
{
    SopInstanceUid = 1,
    FrameOfReferenceUid = 2,
    SeriesInstanceUid = 3,
};

struct DatasetHandle
{
    std::unique_ptr<DcmFileFormat> file;
    int rows = 0;
    int columns = 0;
    int frameCount = 0;
    bool monochrome1 = false;
    std::array<double, 3> imagePosition{};
    std::array<double, 6> imageOrientation{};
    std::array<double, 2> pixelSpacing{};
    double sliceSpacing = 0.0;
    double rescaleSlope = 1.0;
    double rescaleIntercept = 0.0;
    double windowCenter = std::numeric_limits<double>::quiet_NaN();
    double windowWidth = std::numeric_limits<double>::quiet_NaN();
    std::string sopInstanceUid;
    std::string frameOfReferenceUid;
    std::string seriesInstanceUid;
    std::vector<std::int32_t> pixels;
};

void WriteError(char* destination, int capacity, const std::string& message)
{
    if (destination == nullptr || capacity <= 0)
        return;
    const auto count = std::min<std::size_t>(message.size(), static_cast<std::size_t>(capacity - 1));
    std::memcpy(destination, message.data(), count);
    destination[count] = '\0';
}

std::string ConditionMessage(const char* operation, const OFCondition& condition)
{
    std::string result(operation == nullptr ? "DCMTK operation failed" : operation);
    result += ": ";
    result += condition.text();
    return result;
}

bool GetRequiredUint16(DcmDataset& dataset, const DcmTagKey& tag, Uint16& value, const char* name, std::string& error)
{
    const auto condition = dataset.findAndGetUint16(tag, value);
    if (condition.good())
        return true;
    error = ConditionMessage(name, condition);
    return false;
}

bool GetRequiredFloat64Values(
    DcmDataset& dataset,
    const DcmTagKey& tag,
    double* destination,
    unsigned long count,
    const char* name,
    std::string& error)
{
    for (unsigned long index = 0; index < count; ++index)
    {
        Float64 value = 0.0;
        const auto condition = dataset.findAndGetFloat64(tag, value, index);
        if (condition.bad() || !std::isfinite(value))
        {
            error = ConditionMessage(name, condition);
            return false;
        }
        destination[index] = value;
    }
    return true;
}

double GetOptionalFloat64(DcmDataset& dataset, const DcmTagKey& tag, double fallback)
{
    Float64 value = 0.0;
    return dataset.findAndGetFloat64(tag, value, 0).good() && std::isfinite(value)
        ? value
        : fallback;
}

std::string GetOptionalString(DcmDataset& dataset, const DcmTagKey& tag)
{
    OFString value;
    return dataset.findAndGetOFString(tag, value).good() ? value.c_str() : std::string();
}

bool ParsePositiveInteger(const std::string& text, int fallback, int& result)
{
    if (text.empty())
    {
        result = fallback;
        return true;
    }

    errno = 0;
    char* end = nullptr;
    const long parsed = std::strtol(text.c_str(), &end, 10);
    while (end != nullptr && *end == ' ')
        ++end;
    if (errno != 0 || end == text.c_str() || (end != nullptr && *end != '\0') ||
        parsed <= 0 || parsed > std::numeric_limits<int>::max())
        return false;
    result = static_cast<int>(parsed);
    return true;
}

std::int32_t NormalizeStoredPixel(std::uint32_t stored, int bitsStored, int highBit, bool signedPixels)
{
    const int shift = highBit + 1 - bitsStored;
    const std::uint32_t mask = (std::uint32_t{1} << bitsStored) - 1U;
    std::uint32_t value = (stored >> shift) & mask;
    if (signedPixels)
    {
        const std::uint32_t signBit = std::uint32_t{1} << (bitsStored - 1);
        if ((value & signBit) != 0)
            return static_cast<std::int32_t>(value) - static_cast<std::int32_t>(std::uint32_t{1} << bitsStored);
    }
    return static_cast<std::int32_t>(value);
}

bool ReadPixels(DcmDataset& dataset, DatasetHandle& handle, std::string& error)
{
    Uint16 samplesPerPixel = 0;
    Uint16 bitsAllocated = 0;
    Uint16 bitsStored = 0;
    Uint16 highBit = 0;
    Uint16 pixelRepresentation = 0;
    if (!GetRequiredUint16(dataset, DCM_SamplesPerPixel, samplesPerPixel, "SamplesPerPixel", error) ||
        !GetRequiredUint16(dataset, DCM_BitsAllocated, bitsAllocated, "BitsAllocated", error) ||
        !GetRequiredUint16(dataset, DCM_BitsStored, bitsStored, "BitsStored", error) ||
        !GetRequiredUint16(dataset, DCM_HighBit, highBit, "HighBit", error) ||
        !GetRequiredUint16(dataset, DCM_PixelRepresentation, pixelRepresentation, "PixelRepresentation", error))
        return false;

    if (samplesPerPixel != 1 || (bitsAllocated != 8 && bitsAllocated != 16) || bitsStored == 0 ||
        bitsStored > bitsAllocated || highBit >= bitsAllocated || highBit + 1 < bitsStored || pixelRepresentation > 1)
    {
        error = "Only valid 8-bit or 16-bit monochrome integer CT pixels are supported";
        return false;
    }

    std::size_t pixelCount = 0;
    try
    {
        pixelCount = static_cast<std::size_t>(handle.rows) * static_cast<std::size_t>(handle.columns) *
                     static_cast<std::size_t>(handle.frameCount);
        if (pixelCount == 0 || pixelCount > static_cast<std::size_t>(std::numeric_limits<int>::max()))
            throw std::overflow_error("pixel count");
        handle.pixels.resize(pixelCount);
    }
    catch (const std::exception&)
    {
        error = "DICOM pixel count exceeds the supported array size";
        return false;
    }

    const bool signedPixels = pixelRepresentation == 1;
    DcmElement* pixelElement = nullptr;
    const auto elementCondition = dataset.findAndGetElement(DCM_PixelData, pixelElement);
    const auto requiredBytes = pixelCount * static_cast<std::size_t>(bitsAllocated / 8);
    if (elementCondition.bad() || pixelElement == nullptr)
    {
        error = ConditionMessage("PixelData", elementCondition);
        return false;
    }
    const auto availableBytes = static_cast<std::size_t>(pixelElement->getLength());
    if (availableBytes < requiredBytes || availableBytes > requiredBytes + 1U)
    {
        error = "PixelData byte length does not match Rows x Columns x NumberOfFrames";
        return false;
    }

    if (bitsAllocated == 8)
    {
        const Uint8* source = nullptr;
        const auto condition = dataset.findAndGetUint8Array(DCM_PixelData, source);
        if (condition.bad() || source == nullptr)
        {
            error = ConditionMessage("PixelData", condition);
            return false;
        }
        for (std::size_t index = 0; index < pixelCount; ++index)
            handle.pixels[index] = NormalizeStoredPixel(source[index], bitsStored, highBit, signedPixels);
    }
    else
    {
        const Uint16* source = nullptr;
        const auto condition = dataset.findAndGetUint16Array(DCM_PixelData, source);
        if (condition.bad() || source == nullptr)
        {
            error = ConditionMessage("PixelData", condition);
            return false;
        }
        for (std::size_t index = 0; index < pixelCount; ++index)
            handle.pixels[index] = NormalizeStoredPixel(source[index], bitsStored, highBit, signedPixels);
    }
    return true;
}

bool ReadDataset(DatasetHandle& handle, std::string& error)
{
    auto* meta = handle.file->getMetaInfo();
    auto* dataset = handle.file->getDataset();
    if (meta == nullptr || dataset == nullptr)
    {
        error = "DICOM file has no meta information or dataset";
        return false;
    }

    OFString transferSyntax;
    const auto transferCondition = meta->findAndGetOFString(DCM_TransferSyntaxUID, transferSyntax);
    if (transferCondition.bad() || transferSyntax != UID_LittleEndianExplicitTransferSyntax)
    {
        error = "Expected uncompressed Explicit VR Little Endian transfer syntax (1.2.840.10008.1.2.1)";
        return false;
    }

    Uint16 rows = 0;
    Uint16 columns = 0;
    if (!GetRequiredUint16(*dataset, DCM_Rows, rows, "Rows", error) ||
        !GetRequiredUint16(*dataset, DCM_Columns, columns, "Columns", error))
        return false;
    handle.rows = rows;
    handle.columns = columns;

    int frameCount = 1;
    if (!ParsePositiveInteger(GetOptionalString(*dataset, DCM_NumberOfFrames), 1, frameCount))
    {
        error = "NumberOfFrames is not a valid positive integer";
        return false;
    }
    handle.frameCount = frameCount;

    const auto photometric = GetOptionalString(*dataset, DCM_PhotometricInterpretation);
    if (photometric != "MONOCHROME1" && photometric != "MONOCHROME2")
    {
        error = "Only MONOCHROME1 and MONOCHROME2 DICOM images are supported";
        return false;
    }
    handle.monochrome1 = photometric == "MONOCHROME1";

    if (!GetRequiredFloat64Values(*dataset, DCM_ImagePositionPatient, handle.imagePosition.data(), 3,
                                  "ImagePositionPatient", error) ||
        !GetRequiredFloat64Values(*dataset, DCM_ImageOrientationPatient, handle.imageOrientation.data(), 6,
                                  "ImageOrientationPatient", error) ||
        !GetRequiredFloat64Values(*dataset, DCM_PixelSpacing, handle.pixelSpacing.data(), 2,
                                  "PixelSpacing", error))
        return false;
    if (handle.pixelSpacing[0] <= 0.0 || handle.pixelSpacing[1] <= 0.0)
    {
        error = "PixelSpacing must be positive";
        return false;
    }

    handle.sliceSpacing = std::abs(GetOptionalFloat64(*dataset, DCM_SpacingBetweenSlices, 0.0));
    if (handle.sliceSpacing <= 0.0)
        handle.sliceSpacing = std::abs(GetOptionalFloat64(*dataset, DCM_SliceThickness, 0.0));
    if (handle.frameCount > 1 && handle.sliceSpacing <= 0.0)
    {
        error = "Classic multi-frame DICOM requires SpacingBetweenSlices or SliceThickness";
        return false;
    }

    handle.rescaleSlope = GetOptionalFloat64(*dataset, DCM_RescaleSlope, 1.0);
    if (std::abs(handle.rescaleSlope) <= std::numeric_limits<double>::epsilon())
        handle.rescaleSlope = 1.0;
    handle.rescaleIntercept = GetOptionalFloat64(*dataset, DCM_RescaleIntercept, 0.0);
    handle.windowCenter = GetOptionalFloat64(*dataset, DCM_WindowCenter, std::numeric_limits<double>::quiet_NaN());
    handle.windowWidth = GetOptionalFloat64(*dataset, DCM_WindowWidth, std::numeric_limits<double>::quiet_NaN());
    if (handle.windowWidth <= 0.0)
        handle.windowWidth = std::numeric_limits<double>::quiet_NaN();
    handle.sopInstanceUid = GetOptionalString(*dataset, DCM_SOPInstanceUID);
    handle.frameOfReferenceUid = GetOptionalString(*dataset, DCM_FrameOfReferenceUID);
    handle.seriesInstanceUid = GetOptionalString(*dataset, DCM_SeriesInstanceUID);

    return ReadPixels(*dataset, handle, error);
}

DatasetHandle* CastHandle(void* handle)
{
    return static_cast<DatasetHandle*>(handle);
}
} // namespace

extern "C"
{
DENTAL_EXPORT const char* dental_dcmtk_version()
{
    return kRequiredDcmtkVersion;
}

DENTAL_EXPORT int dental_dcmtk_open(const char* path, void** outputHandle, char* error, int errorCapacity)
{
    if (outputHandle != nullptr)
        *outputHandle = nullptr;
    if (path == nullptr || path[0] == '\0' || outputHandle == nullptr)
    {
        WriteError(error, errorCapacity, "DICOM path or output handle is missing");
        return 1;
    }

    try
    {
        auto handle = std::make_unique<DatasetHandle>();
        handle->file = std::make_unique<DcmFileFormat>();
        const auto condition = handle->file->loadFile(path);
        if (condition.bad())
        {
            WriteError(error, errorCapacity, ConditionMessage("loadFile", condition));
            return 2;
        }

        std::string message;
        if (!ReadDataset(*handle, message))
        {
            WriteError(error, errorCapacity, message);
            return 3;
        }

        *outputHandle = handle.release();
        WriteError(error, errorCapacity, std::string());
        return 0;
    }
    catch (const std::exception& exception)
    {
        WriteError(error, errorCapacity, exception.what());
        return 4;
    }
    catch (...)
    {
        WriteError(error, errorCapacity, "Unknown native DICOM decoder failure");
        return 5;
    }
}

DENTAL_EXPORT int dental_dcmtk_get_int(void* opaqueHandle, int field)
{
    const auto* handle = CastHandle(opaqueHandle);
    if (handle == nullptr)
        return -1;
    switch (static_cast<IntegerField>(field))
    {
        case IntegerField::Rows: return handle->rows;
        case IntegerField::Columns: return handle->columns;
        case IntegerField::FrameCount: return handle->frameCount;
        case IntegerField::IsMonochrome1: return handle->monochrome1 ? 1 : 0;
        default: return -1;
    }
}

DENTAL_EXPORT double dental_dcmtk_get_double(void* opaqueHandle, int field, int index)
{
    const auto* handle = CastHandle(opaqueHandle);
    if (handle == nullptr)
        return std::numeric_limits<double>::quiet_NaN();
    switch (static_cast<DoubleField>(field))
    {
        case DoubleField::ImagePositionPatient:
            return index >= 0 && index < 3 ? handle->imagePosition[static_cast<std::size_t>(index)]
                                           : std::numeric_limits<double>::quiet_NaN();
        case DoubleField::ImageOrientationPatient:
            return index >= 0 && index < 6 ? handle->imageOrientation[static_cast<std::size_t>(index)]
                                           : std::numeric_limits<double>::quiet_NaN();
        case DoubleField::PixelSpacing:
            return index >= 0 && index < 2 ? handle->pixelSpacing[static_cast<std::size_t>(index)]
                                           : std::numeric_limits<double>::quiet_NaN();
        case DoubleField::SliceSpacing: return handle->sliceSpacing;
        case DoubleField::RescaleSlope: return handle->rescaleSlope;
        case DoubleField::RescaleIntercept: return handle->rescaleIntercept;
        case DoubleField::WindowCenter: return handle->windowCenter;
        case DoubleField::WindowWidth: return handle->windowWidth;
        default: return std::numeric_limits<double>::quiet_NaN();
    }
}

DENTAL_EXPORT int dental_dcmtk_copy_string(void* opaqueHandle, int field, char* destination, int capacity)
{
    const auto* handle = CastHandle(opaqueHandle);
    if (handle == nullptr || destination == nullptr || capacity <= 0)
        return -1;
    const std::string* value = nullptr;
    switch (static_cast<StringField>(field))
    {
        case StringField::SopInstanceUid: value = &handle->sopInstanceUid; break;
        case StringField::FrameOfReferenceUid: value = &handle->frameOfReferenceUid; break;
        case StringField::SeriesInstanceUid: value = &handle->seriesInstanceUid; break;
        default: return -1;
    }
    const auto count = std::min<std::size_t>(value->size(), static_cast<std::size_t>(capacity - 1));
    std::memcpy(destination, value->data(), count);
    destination[count] = '\0';
    return static_cast<int>(count);
}

DENTAL_EXPORT int dental_dcmtk_copy_frame_pixels_i32(
    void* opaqueHandle,
    int frameIndex,
    std::int32_t* destination,
    int pixelCapacity)
{
    const auto* handle = CastHandle(opaqueHandle);
    if (handle == nullptr || destination == nullptr || frameIndex < 0 || frameIndex >= handle->frameCount)
        return -1;
    const auto pixelsPerFrame = static_cast<std::size_t>(handle->rows) * static_cast<std::size_t>(handle->columns);
    if (pixelsPerFrame > static_cast<std::size_t>(pixelCapacity))
        return -1;
    const auto offset = static_cast<std::size_t>(frameIndex) * pixelsPerFrame;
    std::copy_n(handle->pixels.data() + offset, pixelsPerFrame, destination);
    return static_cast<int>(pixelsPerFrame);
}

DENTAL_EXPORT void dental_dcmtk_close(void* opaqueHandle)
{
    delete CastHandle(opaqueHandle);
}
} // extern "C"
