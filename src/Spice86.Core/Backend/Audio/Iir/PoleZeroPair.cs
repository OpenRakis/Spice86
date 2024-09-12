namespace Spice86.Core.Backend.Audio.Iir;

using System.Numerics;

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

/**
 *
 * It's written on the tin.
 *
 */
public class PoleZeroPair {
    public ComplexPair poles;
    public ComplexPair zeros;

    // single pole/zero
    public PoleZeroPair(Complex p, Complex z) {
        poles = new ComplexPair(p);
        zeros = new ComplexPair(z);
    }

    // pole/zero pair
    public PoleZeroPair(Complex p1, Complex z1, Complex p2, Complex z2) {
        poles = new ComplexPair(p1, p2);
        zeros = new ComplexPair(z1, z2);
    }

    public bool IsSinglePole() {
        return poles.Second.Equals(new Complex(0, 0))
                && zeros.Second.Equals(new Complex(0, 0));
    }

    public bool IsNaN() {
        return poles.IsNaN() || zeros.IsNaN();
    }
};
