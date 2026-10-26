cmake_minimum_required(VERSION 3.22)

project(dental_dcmtk VERSION 1.0.0 LANGUAGES CXX)

if(NOT ANDROID)
    message(FATAL_ERROR "dental_dcmtk is an Android-only Unity native plugin")
endif()

if(NOT CMAKE_ANDROID_ARCH_ABI STREQUAL "arm64-v8a")
    message(FATAL_ERROR "dental_dcmtk v1 supports only arm64-v8a")
endif()

find_package(DCMTK 3.6.8 EXACT CONFIG REQUIRED COMPONENTS dcmdata oflog ofstd)

add_library(dental_dcmtk SHARED DentalDcmtkBridge.cpp)
target_compile_features(dental_dcmtk PRIVATE cxx_std_17)
target_compile_options(dental_dcmtk PRIVATE -fvisibility=hidden -fexceptions -frtti)

if(TARGET DCMTK::dcmdata)
    target_link_libraries(dental_dcmtk PRIVATE DCMTK::dcmdata DCMTK::oflog DCMTK::ofstd)
else()
    # DCMTK 3.6.8 packages built with the legacy export layout do not create namespaced targets.
    target_include_directories(dental_dcmtk PRIVATE ${DCMTK_INCLUDE_DIRS})
    target_link_libraries(dental_dcmtk PRIVATE ${DCMTK_LIBRARIES})
endif()

set_target_properties(dental_dcmtk PROPERTIES
    OUTPUT_NAME dental_dcmtk
    LIBRARY_OUTPUT_DIRECTORY "${DENTAL_DCMTK_OUTPUT_DIRECTORY}"
)
