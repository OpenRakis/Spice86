/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 *  Copyright (c) 2009 by Vinnie Falco
 *  Copyright (c) 2016 by Bernd Porr
 */


using System.Numerics;

namespace Spice86.Core.Backend.Audio.Iir;

/**
 * 
 * A complex pair
 *
 */
public class ComplexPair {

    public Complex first;
    public Complex second;

    public ComplexPair(Complex c1,
                 Complex c2) {
        first = c1;
        second = c2;
    }

    public ComplexPair(Complex c1) {
        first = c1;
        second = new Complex(0, 0);
    }

    public bool isConjugate() {
        return second.Equals(Complex.Conjugate(first));
    }

    public bool isReal() {
        return first.Imaginary == 0 && second.Imaginary == 0;
    }

    // Returns true if this is either a conjugate pair,
    // or a pair of reals where neither is zero.
    public bool isMatchedPair() {
        if (first.Imaginary != 0)
            return second.Equals(Complex.Conjugate(first));
        else
            return second.Imaginary == 0 &&
                    second.Real != 0 &&
                    first.Real != 0;
    }

    public bool is_nan() {
        return Complex.IsNaN(first) || Complex.IsNaN(second);
    }
};
